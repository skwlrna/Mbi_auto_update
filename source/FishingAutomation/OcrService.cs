using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace FishingAutomation;

public enum OcrSignal
{
    None,
    GreatSuccessHint,
    NoSecondRound
}

public sealed class OcrCoordinator
{
    private readonly OcrEngine _engine;
    private readonly SemaphoreSlim _messageGate = new(1, 1);
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    // Windows.Media.Ocr.OcrEngine allows only one RecognizeAsync call at a time.
    // Message OCR and network OCR therefore share this gate.
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    private readonly object _signalGate = new();
    private readonly object _diagGate = new();
    private OcrSignal _lastSignal;
    private DateTime _lastSignalAt;
    private bool _retryDetected;
    private readonly AppLog _log;
    private DateTime _lastOcrDiagnostic = DateTime.MinValue;
    private string _lastOcrDiagnosticText = "";
    // v14: keep OCR useful for troubleshooting without flooding fishing.log.
    // ClearSignal() is called once at the beginning of each fishing round.
    private bool _loggedSecondMessageThisRound;
    private bool _loggedNoSecondMessageThisRound;
    private readonly HashSet<string> _loggedDiagnosticTextsThisRound = new(StringComparer.Ordinal);
    private int _diagnosticCountThisRound;

    public OcrCoordinator(AppLog log)
    {
        _log = log;

        OcrEngine? engine = null;
        Exception? koreanEngineError = null;

        try
        {
            engine = OcrEngine.TryCreateFromLanguage(new Language("ko-KR"));
        }
        catch (Exception ex)
        {
            koreanEngineError = ex;
            _log.Write("ko-KR OCR 초기화 실패, 사용자 언어 OCR로 재시도: " + ex.Message);
        }

        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        _engine = engine ?? throw new InvalidOperationException(
            "Windows OCR 엔진을 만들 수 없습니다. Windows 설정에서 한국어 언어팩/OCR 기능을 확인하세요.",
            koreanEngineError);

        _log.Write("메시지 OCR 준비 완료: 1x -> 2x -> 고대비 2x 순서");
    }

    public void QueueMessage(Bitmap frame, Rectangle roi)
    {
        if (!_messageGate.Wait(0)) return;

        Bitmap? crop1x = null;
        try
        {
            // IMPORTANT: CapturedFrame is disposed by the caller immediately after this
            // method returns.  Never retain or re-read frame from the background task.
            // Snapshot the OCR ROI synchronously while frame is still valid, then derive
            // every later OCR pass from this private bitmap only.
            Rectangle expanded = Rectangle.FromLTRB(roi.Left - 24, roi.Top - 36, roi.Right + 24, roi.Bottom + 18);
            crop1x = CropAndScale(frame, expanded, 1.0);
        }
        catch (Exception ex)
        {
            crop1x?.Dispose();
            _messageGate.Release();
            _log.Write("메시지 OCR 스냅샷 오류: " + ex.Message);
            return;
        }

        Bitmap ownedCrop = crop1x;
        _ = Task.Run(async () =>
        {
            try
            {
                string text1 = await RecognizeAsync(ownedCrop, minimumWordHeightAfterScale: 12);
                if (TryApplySignal(text1, "1x")) return;

                // Scale the private snapshot, not the original game frame.  The old
                // code accessed frame here after CapturedFrame.Dispose(), which caused
                // GDI+ "Object is currently in use elsewhere." intermittently.
                using Bitmap crop2x = ScaleBitmap(ownedCrop, 2.0);
                string text2 = await RecognizeAsync(crop2x, minimumWordHeightAfterScale: 24);
                if (TryApplySignal(text2, "2x")) return;

                using Bitmap highContrast = CreateBrightTextMask(crop2x);
                string text3 = await RecognizeAsync(highContrast, minimumWordHeightAfterScale: 20);
                if (TryApplySignal(text3, "2x-mask")) return;

                string diagnostic = ChooseBestDiagnostic(text1, text2, text3);
                LogOcrDiagnostic(diagnostic);
            }
            catch (Exception ex)
            {
                _log.Write("메시지 OCR 오류: " + ex.Message);
            }
            finally
            {
                ownedCrop.Dispose();
                _messageGate.Release();
            }
        });
    }

    public void QueueNetworkCheck(Bitmap frame, Rectangle roi)
    {
        if (!_networkGate.Wait(0)) return;

        Bitmap? crop = null;
        try
        {
            // Same lifetime rule as message OCR: create an owned snapshot before the
            // CapturedFrame can be disposed by the caller.
            crop = CropAndScale(frame, roi, 1.5);
        }
        catch (Exception ex)
        {
            crop?.Dispose();
            _networkGate.Release();
            _log.Write("네트워크 OCR 스냅샷 오류: " + ex.Message);
            return;
        }

        Bitmap ownedCrop = crop;
        _ = Task.Run(async () =>
        {
            try
            {
                string text = await RecognizeAsync(ownedCrop, minimumWordHeightAfterScale: 0);
                if (FuzzyContains(Normalize(text), Normalize("다시 시도하기"), 2))
                {
                    lock (_signalGate) _retryDetected = true;
                    _log.Write("네트워크 팝업 감지: 다시 시도하기");
                }
            }
            catch (Exception ex)
            {
                _log.Write("네트워크 OCR 오류: " + ex.Message);
            }
            finally
            {
                ownedCrop.Dispose();
                _networkGate.Release();
            }
        });
    }

    public OcrSignal PeekRecentSignal(TimeSpan maxAge)
    {
        lock (_signalGate)
        {
            if (_lastSignal == OcrSignal.None || DateTime.UtcNow - _lastSignalAt > maxAge)
                return OcrSignal.None;
            return _lastSignal;
        }
    }

    public void ClearSignal()
    {
        lock (_signalGate)
        {
            _lastSignal = OcrSignal.None;
            _lastSignalAt = DateTime.MinValue;
            _loggedSecondMessageThisRound = false;
            _loggedNoSecondMessageThisRound = false;
        }
        lock (_diagGate)
        {
            _lastOcrDiagnostic = DateTime.MinValue;
            _lastOcrDiagnosticText = "";
            _loggedDiagnosticTextsThisRound.Clear();
            _diagnosticCountThisRound = 0;
        }
    }

    public bool ConsumeRetry()
    {
        lock (_signalGate)
        {
            bool value = _retryDetected;
            _retryDetected = false;
            return value;
        }
    }

    private bool TryApplySignal(string text, string pass)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string norm = Normalize(text);
        if (string.IsNullOrEmpty(norm)) return false;

        // v12 classification rule:
        //  - "반응이 왔다" / "뭔가 걸렸다" are authoritative NO-SECOND messages.
        //  - "입질이 왔다" / "찌가 흔들렸다" / the two other success hints are SECOND messages.
        // Short Korean words must NOT be compared with a loose edit-distance because
        // "반음" can otherwise look like "입질", and "원가 걸렸다" can look like
        // "찌가 흔들렸다".  Check the no-second family first with constrained fragments.
        if (MatchesNoSecond(norm))
        {
            SetSignal(OcrSignal.NoSecondRound);
            bool shouldLog;
            lock (_signalGate)
            {
                shouldLog = !_loggedNoSecondMessageThisRound;
                _loggedNoSecondMessageThisRound = true;
            }
            if (shouldLog)
                _log.Write($"OCR 2차 없음 메시지[{pass}]: {text}");
            return true;
        }

        if (MatchesSecond(norm))
        {
            SetSignal(OcrSignal.GreatSuccessHint);
            bool shouldLog;
            lock (_signalGate)
            {
                shouldLog = !_loggedSecondMessageThisRound;
                _loggedSecondMessageThisRound = true;
            }
            if (shouldLog)
                _log.Write($"OCR 2차 있음 메시지[{pass}]: {text}");
            return true;
        }

        return false;
    }

    private static bool MatchesNoSecond(string norm)
    {
        // "반응이 왔다" common OCRs seen in the real log:
        //   반음이 왔다 / 반동이 탔다
        // Require the distinctive '반' anchor, then allow up to two total edits.
        // This keeps "입질이 왔다" (which has no '반') out of this family.
        bool reaction = norm.Contains("반", StringComparison.Ordinal) &&
                        FuzzyContains(norm, Normalize("반응이 왔다"), 2);

        // "뭔가 걸렸다" common OCRs:
        //   원가 걸렸다 / 원가 결렸다 / 원가 걸렸나
        // Require a literal 걸/결 anchor before fuzzy whole-phrase matching.  Without
        // this anchor, shorter substrings of "찌가 흔들렸다" can look deceptively
        // close to "뭔가 걸렸다" under Levenshtein distance.
        bool caughtAnchor = norm.Contains("걸", StringComparison.Ordinal) ||
                            norm.Contains("결", StringComparison.Ordinal);
        bool caught = caughtAnchor && FuzzyContains(norm, Normalize("뭔가 걸렸다"), 2);

        return reaction || caught;
    }

    private static bool MatchesSecond(string norm)
    {
        // Keep short phrases strict.  One edit accepts examples such as
        // "입질이 갔다" / "입질이 왓다" without confusing "반응이 왔다".
        if (FuzzyContains(norm, Normalize("입질이 왔다"), 1)) return true;

        // The first token of this message is distinctive.  Allow "씨가" for OCR,
        // but do not accept arbitrary one-edit words such as "원가".
        bool hookWord = norm.Contains("찌", StringComparison.Ordinal) ||
                        norm.Contains("씨", StringComparison.Ordinal);
        if (hookWord && ContainsFuzzyToken(norm, "찌가", 1) &&
            ContainsFuzzyToken(norm, "흔들렸다", 2)) return true;

        if (ContainsFuzzyToken(norm, "묵직", 1) &&
            ContainsFuzzyToken(norm, "느낌", 1) &&
            (norm.Contains("전해", StringComparison.Ordinal) || norm.Contains("전", StringComparison.Ordinal))) return true;

        if (ContainsFuzzyToken(norm, "예감", 1) && ContainsFuzzyToken(norm, "좋다", 1)) return true;
        return false;
    }

    private static bool ContainsFuzzyToken(string text, string target, int distance)
        => FuzzyContains(text, Normalize(target), distance);

    private void LogOcrDiagnostic(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string trimmed = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (trimmed.Length == 0) return;
        if (!trimmed.Any(IsKorean)) return;
        if (trimmed.Length > 100) trimmed = trimmed[..100];

        lock (_diagGate)
        {
            DateTime now = DateTime.UtcNow;
            string key = Normalize(trimmed);
            if (key.Length == 0) return;

            // Same diagnostic text is logged only once per round.  Also cap noisy
            // unmatched OCR to three unique samples per round.
            if (_loggedDiagnosticTextsThisRound.Contains(key)) return;
            if (_diagnosticCountThisRound >= 3) return;
            if (now - _lastOcrDiagnostic < TimeSpan.FromMilliseconds(1200)) return;

            _loggedDiagnosticTextsThisRound.Add(key);
            _diagnosticCountThisRound++;
            _lastOcrDiagnostic = now;
            _lastOcrDiagnosticText = trimmed;
            _log.Write("OCR 진단: " + trimmed);
        }
    }

    private static bool IsKorean(char c)
        => (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u3131' && c <= '\u318E');

    private static string ChooseBestDiagnostic(params string[] texts)
        => texts.Where(t => !string.IsNullOrWhiteSpace(t)).OrderByDescending(t => t.Count(IsKorean)).ThenByDescending(t => t.Length).FirstOrDefault() ?? "";

    private bool SetSignal(OcrSignal signal)
    {
        lock (_signalGate)
        {
            DateTime now = DateTime.UtcNow;

            // An explicit no-second phrase is authoritative.  Once it has been
            // recognized, a later fuzzy second-hint from the same on-screen message
            // must not flip the round back to SECOND.
            if (_lastSignal == OcrSignal.NoSecondRound && signal == OcrSignal.GreatSuccessHint &&
                now - _lastSignalAt < TimeSpan.FromSeconds(4))
                return false;

            _lastSignal = signal;
            _lastSignalAt = now;
            return true;
        }
    }

    private async Task<string> RecognizeAsync(Bitmap bitmap, int minimumWordHeightAfterScale)
    {
        await _engineGate.WaitAsync();
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            byte[] bytes = ms.ToArray();

            using var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            ras.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras);
            using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            OcrResult result = await _engine.RecognizeAsync(softwareBitmap);

            var words = new List<string>();
            foreach (var line in result.Lines)
            foreach (var word in line.Words)
            {
                if (minimumWordHeightAfterScale <= 0 || word.BoundingRect.Height >= minimumWordHeightAfterScale)
                    words.Add(word.Text);
            }
            return string.Join(" ", words);
        }
        finally
        {
            _engineGate.Release();
        }
    }

    private static Bitmap ScaleBitmap(Bitmap source, double scale)
    {
        var dst = new Bitmap(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)),
            PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(dst);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, dst.Width, dst.Height),
            new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return dst;
    }

    private static Bitmap CropAndScale(Bitmap source, Rectangle roi, double scale)
    {
        Rectangle safe = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), roi);
        var dst = new Bitmap(Math.Max(1, (int)(safe.Width * scale)), Math.Max(1, (int)(safe.Height * scale)), PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(dst);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, dst.Width, dst.Height), safe, GraphicsUnit.Pixel);
        return dst;
    }

    private static Bitmap CreateBrightTextMask(Bitmap source)
    {
        var dst = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, source.Width, source.Height);
        BitmapData srcData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData dstData = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int srcBytes = Math.Abs(srcData.Stride) * source.Height;
            int dstBytes = Math.Abs(dstData.Stride) * dst.Height;
            byte[] s = new byte[srcBytes];
            byte[] d = new byte[dstBytes];
            Marshal.Copy(srcData.Scan0, s, 0, s.Length);

            for (int y = 0; y < source.Height; y++)
            {
                int sr = y * Math.Abs(srcData.Stride);
                int dr = y * Math.Abs(dstData.Stride);
                for (int x = 0; x < source.Width; x++)
                {
                    int si = sr + x * 4;
                    int di = dr + x * 4;
                    int b = s[si], g = s[si + 1], r = s[si + 2];
                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    int avg = (r + g + b) / 3;

                    // Message text is neutral gray/white with a dark outline.  Keep
                    // neutral bright glyphs while rejecting most saturated game effects.
                    bool text = avg >= 115 && max - min <= 55;
                    byte v = text ? (byte)255 : (byte)0;
                    d[di] = v;
                    d[di + 1] = v;
                    d[di + 2] = v;
                    d[di + 3] = 255;
                }
            }
            Marshal.Copy(d, 0, dstData.Scan0, d.Length);
        }
        finally
        {
            source.UnlockBits(srcData);
            dst.UnlockBits(dstData);
        }
        return dst;
    }

    private static string Normalize(string s)
    {
        return new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static bool FuzzyContains(string text, string target, int maxDistance)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(text)) return false;
        if (text.Contains(target, StringComparison.Ordinal)) return true;
        int minLen = Math.Max(1, target.Length - maxDistance);
        int maxLen = Math.Min(text.Length, target.Length + maxDistance);
        for (int len = minLen; len <= maxLen; len++)
        {
            for (int i = 0; i + len <= text.Length; i++)
            {
                if (Levenshtein(text.AsSpan(i, len), target.AsSpan()) <= maxDistance)
                    return true;
            }
        }
        return false;
    }

    private static int Levenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int[] prev = Enumerable.Range(0, b.Length + 1).ToArray();
        int[] cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
