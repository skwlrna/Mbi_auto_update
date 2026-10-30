using System.Drawing;
using FishingAutomation;
#if WINDOWS_FIXTURES
using DungeonVisionBot;
#endif

int checks = 0;
void Check(bool ok, string label)
{
    if (!ok) throw new Exception(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
var none = new DailyInterruptionObservation(DailyInterruptionKind.None, Rectangle.Empty);
var stella = new DailyInterruptionObservation(DailyInterruptionKind.StellaPick,
    new(12, 30, 330, 70), new(766, 62));
var notice = new DailyInterruptionObservation(DailyInterruptionKind.Notice,
    new(80, 586, 645, 40), new(701, 345));
var greeting = new DailyInterruptionObservation(DailyInterruptionKind.NpcGreeting,
    new(220, 85, 370, 155), Advance: new(400, 170));

// Same controller as the app; fake transport never sends native input.
foreach (var modal in new[] { stella, notice })
foreach (bool escWorks in new[] { true, false })
{
    var state = modal; int escapes = 0, clicks = 0;
    var recovery = new DailyInterruptionRecovery(
        ct => Task.FromResult(state),
        ct => { escapes++; if (escWorks) state = none; },
        (p, ct) => { Check(p == modal.Close, "fresh modal X, never OS X"); clicks++; state = none; },
        (ms, ct) => Task.CompletedTask, _ => { });
    Check(await recovery.RecoverAsync(default), "known modal handled");
    Check(escapes == 1 && clicks == (escWorks ? 0 : 1), "ESC once, inspect, X fallback only if needed");
    // Simulate next reset, or the same popup reappearing after any unknown time.
    state = modal;
    Check(await recovery.RecoverAsync(default), "no daily suppression after repeat/reset");
    Check(escapes == 2, "later popup gets fresh handling");
}
{
    int clicks = 0, escapes = 0;
    var state = greeting;
    // Notice behind greeting is cleared by ESC, without leaking work input.
    var chain = new DailyInterruptionRecovery(ct => Task.FromResult(state),
        ct => { escapes++; state = none; }, (p, ct) =>
        {
            clicks++;
            if (clicks == 1) state = greeting with { Skip = new(700, 90) };
            else { Check(p == state.Skip, "NPC skip uses current geometry"); state = notice; }
        }, (ms, ct) => Task.CompletedTask, _ => { });
    Check(await chain.RecoverAsync(default), "NPC then notice chain handled before return");
    Check(clicks == 2 && escapes == 1, "two NPC clicks and one notice ESC");
}
{
    int inputs = 0;
    var recovery = new DailyInterruptionRecovery(ct => Task.FromResult(none),
        ct => inputs++, (p, ct) => inputs++, (ms, ct) => Task.CompletedTask, _ => { });
    Check(!await recovery.RecoverAsync(default) && inputs == 0, "unknown/normal screen sends no input");
}
{
    int inputs = 0;
    var recovery = new DailyInterruptionRecovery(ct => Task.FromResult(stella with { Close = null }),
        ct => inputs++, (p, ct) => inputs++, (ms, ct) => Task.CompletedTask, _ => { });
    try { await recovery.RecoverAsync(default); throw new Exception("must reject blocked modal"); }
    catch (InvalidOperationException) { Check(inputs == 1, "persistent modal never gets repeated ESC or guessed X"); }
}
{
    using var cts = new CancellationTokenSource(); int inputs = 0;
    var recovery = new DailyInterruptionRecovery(ct => Task.FromResult(notice),
        ct => inputs++, (p, ct) => inputs++, (ms, ct) => { cts.Cancel(); return Task.CompletedTask; }, _ => { });
    try { await recovery.RecoverAsync(cts.Token); throw new Exception("must cancel"); }
    catch (OperationCanceledException) { Check(inputs == 0, "F10 during verification sends zero input"); }
}
{
    int inputs = 0, reads = 0;
    var recovery = new DailyInterruptionRecovery(ct => Task.FromResult(++reads % 2 == 1 ? stella : none),
        ct => inputs++, (p, ct) => inputs++, (ms, ct) => Task.CompletedTask, _ => { });
    try { await recovery.RecoverAsync(default); }
    catch (InvalidOperationException) { }
    Check(inputs == 0, "single-frame false positive never authorizes input");
}
{
    int queries = 0, polls = 0;
    var cli = new MabinogiMobileCli(new AppLog(Path.Combine(Path.GetTempPath(), "daily-test.log"), 10000), true,
        (command, ct) => { queries++; return Task.FromResult(new CliProcessOutput(0,
            "{\"fresh\":" + queries + "}", "")); });
    using (ProductionInterruptionCheckpoint.Install(ct => Task.FromResult(++polls == 2)))
    {
        var result = await cli.GetActivityAsync();
        Check(queries == 2 && result.Data!.Value.GetProperty("fresh").GetInt32() == 2,
            "modal during CLI read discards stale response and repeats read only");
        Check((await cli.QueryAsync("execute_altering")).State == "blocked" && queries == 2,
            "modal handler never opens CLI action boundary");
    }
    int before = polls;
    await cli.GetItemsAsync();
    Check(polls == before, "disposed scope stops modal inputs outside the run");
}
{
    using var run = new CancellationTokenSource(); int polls = 0;
    using var scope = ProductionInterruptionCheckpoint.Install(ct =>
        { polls++; return Task.FromResult(false); }, run.Token);
    run.Cancel();
    await ProductionInterruptionCheckpoint.CheckAsync(CancellationToken.None);
    Check(polls == 0, "cleanup read token cannot re-arm modal inputs after F10");
}
#if WINDOWS_FIXTURES
foreach (string label in new[] { "건너뛰기", "대화 건너뛰기", "대화 넘기기", "넘기기", "스킵", "대화 스킵", "SKIP", "대화 건너뛰기 (ESC)" })
    Check(DailyInterruptionScreen.IsNpcSkipLabel(label), "NPC skip wording variant: " + label);
foreach (string label in new[] { "퀘스트 건너뛰기", "확인", "결제", "오늘도" })
    Check(!DailyInterruptionScreen.IsNpcSkipLabel(label), "unrelated right-side text rejected: " + label);
foreach (string name in new[] { "stella", "greeting", "notice", "travel", "hub" })
{
    using var image = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".png"));
    // Recorded OCR lines isolate recognition/control integration from WinRT
    // language-pack availability on hosted CI. Pixel tests use real fixtures.
    var detector = new DailyInterruptionScreen((frame, roi, scale, ct) =>
    {
        string text = name switch
        {
            "stella" when roi.Y == 30 => "오늘의 스텔라 픽",
            "notice" => "공지사항 이벤트 쿠폰 입력 FAQ",
            "greeting" when roi.X == 220 => "오늘도 와 줘서 고마워",
            _ => ""
        };
        IReadOnlyList<DetectionResult> lines = text.Length == 0 ? Array.Empty<DetectionResult>() :
            new[] { new DetectionResult(true, roi, 1, text) };
        return Task.FromResult(lines);
    });
    var observed = await detector.ObserveAsync(image, default);
    var expected = name switch { "stella" => DailyInterruptionKind.StellaPick,
        "notice" => DailyInterruptionKind.Notice, "greeting" => DailyInterruptionKind.NpcGreeting,
        _ => DailyInterruptionKind.None };
    Check(observed.Kind == expected, name + " complete detector with recorded OCR");
    Check(DailyInterruptionScreen.HasGreetingBubble(image) == (name == "greeting"), name + " greeting structure");
    int row = DailyInterruptionScreen.NoticeTabsRow(image);
    Check((row >= 0) == (name == "notice"), name + " notice chrome, variable content ignored");
    if (name == "notice")
    {
        var close = DailyInterruptionScreen.FindNoticeClose(image, row);
        Check(close is not null && Math.Abs(close.Value.X - 701) < 8 && Math.Abs(close.Value.Y - 345) < 8,
            "small web X found above panel, outside notice content");
        using var changed = (Bitmap)image.Clone();
        using (var g = Graphics.FromImage(changed)) g.FillRectangle(Brushes.Magenta, 95, 365, 590, 210);
        Check(DailyInterruptionScreen.NoticeTabsRow(changed) >= 0, "changed banner/body retains notice recognition");
    }
    if (name == "stella")
        Check(DailyInterruptionScreen.FindCloseGlyph(image, new(740, 32, 52, 62), 7, 13) is not null,
            "Stella close glyph on actual incident frame");
}
#endif
Console.WriteLine($"{checks} daily interruption checks passed");
