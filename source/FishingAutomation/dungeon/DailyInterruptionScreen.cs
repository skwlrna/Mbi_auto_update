using FishingAutomation;

namespace DungeonVisionBot;

// Recognition is tied to the three observed screens, never Unknown/Dialogue
// alone. Advertising artwork, dates and notice body text are not anchors.
internal sealed class DailyInterruptionScreen
{
    private readonly Func<Bitmap, Rectangle, int, CancellationToken,
        Task<IReadOnlyList<DetectionResult>>> _readLines;
    private bool _greetingActive;
    internal DailyInterruptionScreen(OcrRecognizer ocr) => _readLines = ocr.ReadLinesAsync;
    internal DailyInterruptionScreen(Func<Bitmap, Rectangle, int, CancellationToken,
        Task<IReadOnlyList<DetectionResult>>> readLines) => _readLines = readLines;
    private static readonly DailyInterruptionObservation None =
        new(DailyInterruptionKind.None, Rectangle.Empty);

    internal async Task<DailyInterruptionObservation> ObserveAsync(Bitmap frame, CancellationToken ct)
    {
        if (frame.Size != new Size(800, 1000)) return None;
        ct.ThrowIfCancellationRequested();
        if (FindCloseGlyph(frame, new Rectangle(740, 32, 52, 62), 7, 13) is Point stellaClose &&
            await Text(frame, new(12, 30, 330, 70), "오늘의 스텔라 픽", ct))
            return new(DailyInterruptionKind.StellaPick, new(12, 30, 330, 70), stellaClose);

        int tabsY = NoticeTabsRow(frame);
        if (tabsY >= 0)
        {
            var tabs = new Rectangle(80, Math.Max(0, tabsY - 12), 645, 45);
            var lines = await _readLines(frame, tabs, 2, ct).ConfigureAwait(false);
            string tabText = string.Concat(lines.Select(x => FuzzyText.Normalize(x.ReadText ?? "")));
            int labels = new[] { "공지사항", "이벤트", "쿠폰입력", "faq" }
                .Count(tabText.Contains);
            if (labels >= 2)
            {
                // Small embedded-web close control above the panel. The
                // application title bar lies OUTSIDE the captured client.
                Point? close = FindNoticeClose(frame, tabsY);
                return new(DailyInterruptionKind.Notice, new(80, tabsY, 645, 40), close);
            }
        }

        bool bubble = HasGreetingBubble(frame);
        if (!bubble && !_greetingActive) return None;
        Point? skip = null;
        if (_greetingActive || bubble)
        {
            var rightLines = await _readLines(frame, new(500, 15, 295, 935), 2, ct)
                .ConfigureAwait(false);
            var hits = rightLines.Where(x => IsNpcSkipLabel(x.ReadText ?? "")).ToArray();
            if (hits.Length == 1) skip = hits[0].Center;
        }
        if (!_greetingActive && bubble)
        {
            var bubbleLines = await _readLines(frame, new(220, 85, 370, 155), 2, ct)
                .ConfigureAwait(false);
            string bubbleText = string.Concat(bubbleLines.Select(x => FuzzyText.Normalize(x.ReadText ?? "")));
            _greetingActive = new[] { "오늘도", "어서와", "반가워", "잘왔어" }.Any(bubbleText.Contains);
        }
        if (_greetingActive && (bubble || skip is not null))
            return new(DailyInterruptionKind.NpcGreeting, new(220, 85, 370, 155),
                Advance: bubble ? new Point(400, 170) : null, Skip: skip);
        _greetingActive = false;
        return None;
    }

    private async Task<bool> Text(Bitmap frame, Rectangle area, string text, CancellationToken ct)
        => (await _readLines(frame, area, 2, ct).ConfigureAwait(false))
            .Any(x => FuzzyText.ContainsApprox(x.ReadText ?? "", text, 0));

    internal static bool IsNpcSkipLabel(string text)
    {
        string normalized = FuzzyText.Normalize(text);
        if (normalized.EndsWith("esc", StringComparison.Ordinal)) normalized = normalized[..^3];
        return normalized is "건너뛰기" or "대화건너뛰기" or "대화넘기기" or
            "넘기기" or "스킵" or "대화스킵" or "skip";
    }

    internal static bool HasGreetingBubble(Bitmap frame)
    {
        if (frame.Size != new Size(800, 1000)) return false;
        // Observed NPC cinematic: white rounded speech bubble at top-center,
        // lime NPC-name badge above its left edge. Ordinary field chat and
        // facility/travel dialogs do not have this combination.
        int white = 0, samples = 0, badge = 0;
        for (int y = 112; y < 182; y += 7)
        for (int x = 268; x < 532; x += 8)
        { if (Bright(frame.GetPixel(x, y), 225)) white++; samples++; }
        for (int y = 90; y < 115; y += 3)
        for (int x = 270; x < 338; x += 4)
        {
            var p = frame.GetPixel(x, y);
            if (p.R > 100 && p.G > 130 && p.B < 110 && p.G > p.R * .85) badge++;
        }
        return white > samples * .65 && badge >= 25;
    }

    internal static int NoticeTabsRow(Bitmap frame)
    {
        if (frame.Size != new Size(800, 1000)) return -1;
        for (int y = 350; y < 830; y += 4)
        {
            var green = frame.GetPixel(160, y);
            var orange = frame.GetPixel(320, y);
            var purple = frame.GetPixel(480, y);
            var blue = frame.GetPixel(640, y);
            if (!(green.G > green.R + 12 && green.G > 100 &&
                  orange.R > orange.B + 45 && orange.R > orange.G + 20 &&
                  purple.B > purple.G + 12 && purple.R > purple.G + 8 &&
                  blue.B > blue.R + 15 && blue.G > blue.R + 5)) continue;
            int light = 0;
            foreach (int x in new[] { 160, 320, 480, 640 })
                if (Bright(frame.GetPixel(x, y + 44), 205)) light++;
            if (light >= 3) return y;
        }
        return -1;
    }

    internal static Point? FindCloseGlyph(Bitmap frame, Rectangle roi, int minRadius, int maxRadius)
    {
        roi = Rectangle.Intersect(roi, new Rectangle(Point.Empty, frame.Size));
        for (int r = minRadius; r <= maxRadius; r++)
        for (int y = roi.Top + r + 2; y < roi.Bottom - r - 2; y++)
        for (int x = roi.Left + r + 2; x < roi.Right - r - 2; x++)
        {
            int ink = minRadius <= 3 ? 95 : 175;
            if (!Bright(frame.GetPixel(x, y), minRadius <= 3 ? 145 : 175)) continue;
            int arms = 0;
            foreach (int d in new[] { -r, -r + 1, r - 1, r })
            {
                if (NearBright(frame, x + d, y + d, ink)) arms++;
                if (NearBright(frame, x + d, y - d, ink)) arms++;
            }
            if (arms < 7) continue;
            // Empty axial gaps exclude white blocks and a '+' glyph.
            int gaps = 0;
            foreach (var p in new[] { new Point(x - r, y), new Point(x + r, y),
                         new Point(x, y - r), new Point(x, y + r) })
                if (!Bright(frame.GetPixel(p.X, p.Y), minRadius <= 3 ? 90 : 150)) gaps++;
            if (gaps == 4) return new Point(x, y);
        }
        return null;
    }

    internal static Point? FindNoticeClose(Bitmap frame, int tabsY)
    {
        // Locate the scrollbar's light right rail, then search only just above
        // that panel edge. A quest-text X or an OS close control cannot qualify.
        for (int x = 692; x <= 712; x += 2)
        for (int y = Math.Max(160, tabsY - 350); y < tabsY - 120; y++)
        {
            if (!Bright(frame.GetPixel(x, y), 180) ||
                Bright(frame.GetPixel(x, y - 8), 130)) continue;
            int light = 0, samples = 0;
            for (int yy = y + 10; yy < tabsY + 35; yy += 10)
            { if (Bright(frame.GetPixel(x, yy), 175)) light++; samples++; }
            if (light < samples * .9) continue;
            var close = FindCloseGlyph(frame, new Rectangle(x - 18, y - 32, 38, 30), 3, 6);
            if (close is not null) return close;
        }
        return null;
    }

    private static bool NearBright(Bitmap frame, int x, int y, int threshold)
        => Bright(frame.GetPixel(x, y), threshold) || Bright(frame.GetPixel(x + 1, y), threshold) ||
           Bright(frame.GetPixel(x - 1, y), threshold) || Bright(frame.GetPixel(x, y + 1), threshold) ||
           Bright(frame.GetPixel(x, y - 1), threshold);
    private static bool Bright(Color p, int threshold)
        => Math.Min(p.R, Math.Min(p.G, p.B)) >= threshold;
}
