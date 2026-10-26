using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Windows.Globalization;
using Windows.Media.Ocr;

internal static class MaterialTransferRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static;
    private sealed class PromptRejectedException(string message) : Exception(message) { }

    internal static async Task<int> RunAsync(Assembly production)
    {
        Type screen = production.GetType("DungeonVisionBot.AlteringScreen", true)!;
        MethodInfo flow = screen.GetMethod("RunMaterialTransferPromptAsync", All)!;
        MethodInfo visual = screen.GetMethod("HasMaterialTransferModalVisual", All)!;
        MethodInfo selected = screen.GetMethod("MaterialTransferCheckboxLooksSelected", All)!;
        bool Pixel(MethodInfo method, Bitmap frame) => (bool)method.Invoke(null, new object[] { frame })!;
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Material transfer FAILED: " + name);
            checks++;
            Console.WriteLine("PASS material transfer: " + name);
        }

        string fixture = Path.Combine(AppContext.BaseDirectory, "material-transfer-fixtures",
            "unchecked-20261026-115823.png");
        using var uncheckedFrame = new Bitmap(fixture);
        using var checkedFrame = (Bitmap)uncheckedFrame.Clone();
        using (var g = Graphics.FromImage(checkedFrame))
        using (var brush = new SolidBrush(Color.FromArgb(10, 177, 155)))
            g.FillRectangle(brush, 305, 839, 23, 23);
        using var changedGrey = (Bitmap)uncheckedFrame.Clone();
        using (var g = Graphics.FromImage(changedGrey))
            g.FillRectangle(Brushes.Gray, 305, 839, 23, 23);
        using var absent = new Bitmap(800, 1000);
        Check(Pixel(visual, uncheckedFrame) && !Pixel(selected, uncheckedFrame),
            "unaltered incident capture is a visible, unchecked material prompt");
        Check(Pixel(selected, checkedFrame) && !Pixel(selected, changedGrey),
            "synthetic green state is selected; a grey pixel change is not");

        Point checkbox = new(315, 851), confirm = new(500, 915);
        async Task Scenario(string name, Bitmap[] frames, Point[] expectedClicks,
            string? expectedFailure = null, int rejectVerification = 0,
            int cancelVerification = 0, int expectedVerifications = -1)
        {
            int captures = 0, verifications = 0;
            var clicks = new List<Point>();
            using var cancellation = new CancellationTokenSource();
            Func<CancellationToken, Bitmap> capture = token =>
            {
                token.ThrowIfCancellationRequested();
                if (captures >= frames.Length)
                    throw new InvalidOperationException(name + ": unexpected extra capture");
                return (Bitmap)frames[captures++].Clone();
            };
            Func<Bitmap, CancellationToken, Task<bool>> verify = (frame, token) =>
            {
                token.ThrowIfCancellationRequested();
                verifications++;
                if (verifications == cancelVerification) cancellation.Cancel();
                return Task.FromResult(Pixel(visual, frame) && verifications != rejectVerification);
            };
            // Records inputs only. No window, Interception driver or game input
            // object is created anywhere in this test.
            Action<Point, CancellationToken> click = (point, _) => clicks.Add(point);
            Func<int, CancellationToken, Task> delay = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };
            Func<Bitmap, string, Exception> failure = (_, message) => new PromptRejectedException(message);
            Action<string> log = _ => { };
            string? rejected = null;
            bool canceled = false;
            try
            {
                await (Task)flow.Invoke(null, new object[]
                    { capture, verify, click, delay, failure, log, cancellation.Token })!;
            }
            catch (PromptRejectedException error) { rejected = error.Message; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
            Check(clicks.SequenceEqual(expectedClicks), name + ": exact click order/count");
            Check(expectedFailure is null ? rejected is null :
                rejected?.Contains(expectedFailure, StringComparison.Ordinal) == true,
                name + ": expected stop reason");
            Check(canceled == (cancelVerification > 0), name + ": cancellation outcome");
            Check(captures == frames.Length, name + ": expected capture sequence consumed");
            if (expectedVerifications >= 0)
                Check(verifications == expectedVerifications, name + ": expected verification count");
        }

        Bitmap u = uncheckedFrame, s = checkedFrame, n = absent;
        await Scenario("unchecked -> green -> confirm", new[] { u, u, s, s, n }, new[] { checkbox, confirm },
            expectedVerifications: 4);
        await Scenario("already green never toggles", new[] { s, s, s, n }, new[] { confirm },
            expectedVerifications: 3);
        await Scenario("popup absent", Enumerable.Repeat(n, 6).ToArray(), Array.Empty<Point>(),
            expectedVerifications: 0);
        await Scenario("delayed popup", new[] { n, n, u, u, s, s, n }, new[] { checkbox, confirm });
        await Scenario("unchecked -> checked between frames", new[] { u, s }, Array.Empty<Point>(), "두 프레임");
        await Scenario("checked -> unchecked between frames", new[] { s, u }, Array.Empty<Point>(), "두 프레임");
        await Scenario("click did not select", new[] { u, u, u }, new[] { checkbox }, "초록색으로");
        await Scenario("changed pixels are still grey", new[] { u, u, changedGrey }, new[] { checkbox }, "초록색으로");
        await Scenario("selection lost before confirm", new[] { u, u, s, u }, new[] { checkbox }, "직전");
        await Scenario("preselected state lost before confirm", new[] { s, s, u }, Array.Empty<Point>(), "직전");
        await Scenario("first OCR rejected", new[] { u }, Array.Empty<Point>(), "문구", rejectVerification: 1);
        await Scenario("second OCR rejected", new[] { u, u }, Array.Empty<Point>(), "새 프레임", rejectVerification: 2);
        await Scenario("post-click OCR rejected", new[] { u, u, s }, new[] { checkbox }, "초록색으로", rejectVerification: 3);
        await Scenario("final OCR rejected", new[] { u, u, s, s }, new[] { checkbox }, "직전", rejectVerification: 4);
        await Scenario("cancel before checkbox", new[] { u, u }, Array.Empty<Point>(), cancelVerification: 2);
        await Scenario("cancel before confirm", new[] { u, u, s, s }, new[] { checkbox }, cancelVerification: 4);
        await Scenario("confirm did not close popup",
            new[] { s, s, s }.Concat(Enumerable.Repeat(s, 16)).ToArray(), new[] { confirm }, "반복 클릭 금지");

        // The dedicated Windows OCR workflow MUST run this block. Other L2 jobs
        // can exercise the deterministic flow without installing an OS language.
        if (Environment.GetEnvironmentVariable("MBI_REQUIRE_MATERIAL_TRANSFER_OCR") != "1")
        {
            Console.WriteLine("SKIP material transfer OCR: run the required Korean OCR workflow for incident-image proof");
            return checks;
        }
        Check(OcrEngine.IsLanguageSupported(new Language("ko-KR")), "Korean Windows OCR is installed (no language fallback)");
        Type runtimeType = production.GetType("DungeonVisionBot.ProductionUiRuntime", true)!;
        Type ocrType = production.GetType("DungeonVisionBot.OcrRecognizer", true)!;
        Type evidenceType = production.GetType("DungeonVisionBot.ProductionEvidenceBuffer", true)!;
        // Construct only OCR + diagnostics. Bypass constructors that attach to a
        // live window or initialize an input driver, just like the existing L2 probes.
        object runtime = RuntimeHelpers.GetUninitializedObject(runtimeType);
        object ocr = Activator.CreateInstance(ocrType, nonPublic: true)!;
        using var evidence = (IDisposable)Activator.CreateInstance(evidenceType, All, null,
            new object[] { Path.GetTempPath(), "material-transfer-regression" }, null)!;
        runtimeType.GetField("_ocr", All)!.SetValue(runtime, ocr);
        runtimeType.GetField("_evidence", All)!.SetValue(runtime, evidence);
        object probe = RuntimeHelpers.GetUninitializedObject(screen);
        screen.GetField("_ui", All)!.SetValue(probe, runtime);
        MethodInfo prompt = screen.GetMethod("IsExactMaterialTransferPromptAsync", All)!;
        async Task<bool> Verify(Bitmap frame) => await (Task<bool>)prompt.Invoke(probe,
            new object[] { frame, CancellationToken.None })!;
        bool incidentAccepted = await Verify(uncheckedFrame);
        if (!incidentAccepted)
        {
            foreach (var (label, region) in new[]
            {
                ("title", new Rectangle(258, 665, 350, 62)),
                ("weekly", new Rectangle(334, 827, 250, 51)),
                ("confirm", new Rectangle(420, 885, 160, 64))
            })
            {
                var readLines = ocrType.GetMethod("ReadLinesAsync", All)!;
                var dimLines = ocrType.GetMethod("ReadDimLinesAsync", All)!;
                var regular = await (Task<IReadOnlyList<DungeonVisionBot.DetectionResult>>)readLines.Invoke(
                    ocr, new object[] { uncheckedFrame, region, 2, CancellationToken.None })!;
                var dimmed = await (Task<IReadOnlyList<DungeonVisionBot.DetectionResult>>)dimLines.Invoke(
                    ocr, new object[] { uncheckedFrame, region, CancellationToken.None })!;
                Console.WriteLine("OCR INCIDENT DIAGNOSTIC " + label + " raw=" +
                    string.Join(" | ", regular.Select(x => x.Text)) + " dim=" +
                    string.Join(" | ", dimmed.Take(20).Select(x => x.Text)));
            }
        }
        Check(incidentAccepted, "actual incident capture passes the production OCR predicate");
        Check(await Verify(checkedFrame), "checkbox state change preserves production OCR identity");

        // Each text ROI is independently required. Preserve the shell and enough
        // white pixels to pass the visual prefilter so OCR, not geometry, must veto.
        foreach (var roi in new[] { new Rectangle(258, 665, 350, 62),
                     new Rectangle(334, 827, 250, 51), new Rectangle(420, 885, 160, 64) })
        {
            using var masked = (Bitmap)uncheckedFrame.Clone();
            using (var g = Graphics.FromImage(masked))
            using (var background = new SolidBrush(roi.Y == 885
                       ? Color.FromArgb(6, 178, 114) : Color.FromArgb(34, 40, 50)))
            {
                g.FillRectangle(background, roi);
                g.FillRectangle(Brushes.White, roi.X + 20, roi.Y + 10, 50, 4);
            }
            Check(Pixel(visual, masked), "masked ROI still passes independent modal prefilter: " + roi);
            Check(!await Verify(masked), "missing text ROI blocks production OCR predicate: " + roi);
        }
        Check(!await Verify(absent), "absent modal cannot pass production OCR predicate");
        return checks;
    }
}
