using System.Numerics;
using DungeonVisionBot;

static class Program
{
    private static int _passed;
    private static readonly DateTimeOffset T = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static WaterwayEvidence Screen(bool yes = true, int seconds = 0, string label = "screen") =>
        new(yes, WaterwayObservationOrigin.Screen, T.AddSeconds(seconds), label);
    private static WaterwayEvidence Guess(int seconds = 0) =>
        new(true, WaterwayObservationOrigin.Inference, T.AddSeconds(seconds), "inferred");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static WaterwayTile[] Empty() => Enumerable.Repeat(WaterwayTile.Empty, 9).ToArray();

    private static void Main()
    {
        Test("flow/only-confirmed-entry", () =>
        {
            var flow = new ScatteredWaterwayFlow();
            Require(!flow.TryBeginNewEntry("round-a", Guess()), "inferred entry was accepted");
            Require(flow.TryBeginNewEntry("round-a", Screen()), "direct entry was rejected");
            Require(!flow.TryBeginNewEntry("round-a", Screen(seconds: 1)), "duplicate entry reset");
            Require(flow.Stage == WaterwayStage.Room1Spear, "wrong start stage");
            Require(!flow.TryAdvance(flow.EntryEpoch, WaterwayStage.Room3Tiles,
                WaterwayMilestone.Room3Entered, Screen(seconds: 1)), "stage jump accepted");
            Require(!flow.TryAdvance(flow.EntryEpoch, WaterwayStage.Room1Cleared,
                WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen, Guess(seconds: 1)),
                "inferred finish accepted");
            Require(!flow.TryAdvance(flow.EntryEpoch, WaterwayStage.Room1Cleared,
                WaterwayMilestone.Room3Entered, Screen(seconds: 1)), "wrong evidence accepted");
        });
        Test("flow/all-rooms-then-result", () =>
        {
            var flow = new ScatteredWaterwayFlow();
            Require(flow.TryBeginNewEntry("first", Screen()), "entry failed");
            var milestones = new[]
            {
                (WaterwayStage.Room1Cleared, WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen),
                (WaterwayStage.Room2Combat, WaterwayMilestone.Room2CombatEntered),
                (WaterwayStage.Room2Cleared, WaterwayMilestone.Room2ClearedAndNextAreaOpen),
                (WaterwayStage.Room3Tiles, WaterwayMilestone.Room3Entered),
                (WaterwayStage.BossDefeated, WaterwayMilestone.TerosaDefeated),
                (WaterwayStage.ResultPrompt, WaterwayMilestone.TouchResultPromptVisible),
                (WaterwayStage.RewardConfirmed, WaterwayMilestone.RewardScreenVisible)
            };
            for (int i = 0; i < milestones.Length; i++)
                Require(flow.TryAdvance(flow.EntryEpoch, milestones[i].Item1,
                    milestones[i].Item2, Screen(seconds: i + 1)), "milestone rejected " + i);
            Require(flow.History.Count == 7 && flow.Stage == WaterwayStage.RewardConfirmed,
                "result never confirmed");
            Require(!flow.TryAdvance(flow.EntryEpoch, WaterwayStage.Room1Cleared,
                WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen, Screen(seconds: 30)),
                "late spear packet regressed finished run");
            long priorEpoch = flow.EntryEpoch;
            Require(flow.TryBeginNewEntry("second", Screen(seconds: 50)), "new entry failed");
            Require(flow.Stage == WaterwayStage.Room1Spear && flow.History.Count == 0,
                "new entry not reset");
            Require(!flow.TryAdvance(priorEpoch, WaterwayStage.Room1Cleared,
                WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen, Screen(seconds: 51)),
                "previous run's event accepted");
            Require(!flow.TryAdvance(flow.EntryEpoch, WaterwayStage.Room1Cleared,
                WaterwayMilestone.Room1EnemiesDefeatedAndDoorOpen, Screen(seconds: 20)),
                "stale packet accepted");
        });
        Test("observations/freshness-and-inference", () =>
        {
            Require(Screen().IsFresh(T.AddMilliseconds(300), TimeSpan.FromSeconds(1)), "fresh frame lost");
            Require(!Screen().IsFresh(T.AddSeconds(2), TimeSpan.FromSeconds(1)), "stale frame accepted");
            Require(!Guess().IsFresh(T, TimeSpan.FromSeconds(1)), "guess treated as direct");
        });
        Test("spear/behind-and-hit-line", () =>
        {
            Vector2 s = new(0, 0), m = new(0, 10), p = new(0, 20);
            Require(WaterwaySpearGeometry.TargetBehind(s, m, 5) == new Vector2(0, 15),
                "behind geometry wrong");
            Require(WaterwaySpearGeometry.IsInLine(s, m, p, 0.5f, 1f),
                "collinear target rejected");
            Require(!WaterwaySpearGeometry.IsInLine(s, m, new Vector2(10, 20), 0.5f, 1f),
                "miss alignment accepted");
            Require(!WaterwaySpearGeometry.IsInLine(s, m, new Vector2(0, 9), 0.5f, 1f),
                "player not behind shaman accepted");
            Require(WaterwaySpearGeometry.TargetBehind(s, s, 1) is null,
                "zero-length thrower-to-shaman accepted");
            Require(WaterwaySpearGeometry.TargetBehind(s, m, 0) is null,
                "unmeasured setback accepted");
        });
        Test("tiles/win-before-block", () =>
        {
            var b = Empty();
            b[0] = b[1] = WaterwayTile.O;
            b[3] = b[4] = WaterwayTile.X;
            Require(WaterwayTilePlanner.Choose(b, true, true, 6) == 2, "winning tile not first");
        });
        Test("tiles/block-before-corner", () =>
        {
            var b = Empty();
            b[0] = b[1] = WaterwayTile.X;
            b[3] = WaterwayTile.O;
            Require(WaterwayTilePlanner.Choose(b, true, true, 6) == 2, "block not first");
        });
        Test("tiles/opposite-corner", () =>
        {
            var b = Empty(); b[0] = WaterwayTile.O;
            Require(WaterwayTilePlanner.Choose(b, true, true, 0) == 8,
                "opposite corner not preferred");
            b[8] = WaterwayTile.X;
            Require(WaterwayTilePlanner.Choose(b, true, true, 0) == 2,
                "blocked opposite was selected");
        });
        Test("tiles/center-after-corners", () =>
        {
            var b = Empty();
            b[0] = b[2] = b[6] = b[8] = WaterwayTile.X;
            Require(WaterwayTilePlanner.Choose(b, true, true, null) == 4,
                "center not used");
        });
        Test("tiles/never-move-on-other-turn-or-unknown", () =>
        {
            var b = Empty(); b[4] = WaterwayTile.X;
            Require(WaterwayTilePlanner.Choose(b, true, false, null) is null, "other player turn moved");
            Require(WaterwayTilePlanner.Choose(b, false, true, null) is null, "X turn moved");
            b[6] = WaterwayTile.Unknown;
            Require(WaterwayTilePlanner.Choose(b, true, true, null) is null,
                "partially read board accepted");
        });
        Test("tiles/party-O-contributes-to-win", () =>
        {
            var b = Empty();
            b[0] = b[4] = WaterwayTile.O; // may be from teammates
            Require(WaterwayTilePlanner.Choose(b, true, true, null) == 8,
                "teammate O ignored");
        });
        Test("board/no-optimistic-placement", () =>
        {
            var tracker = new WaterwayBoardTracker();
            var b = Empty();
            Require(tracker.TryObserve(b, Screen()), "initial board rejected");
            Require(!tracker.ConfirmOwnO(0, Screen()), "standing on a pad counted as O");
            b[0] = WaterwayTile.O;
            Require(tracker.TryObserve(b, Screen(seconds: 1)), "new O rejected");
            Require(tracker.ConfirmOwnO(0, Screen(seconds: 1)) &&
                tracker.LastConfirmedOwnCorner == 0, "confirmed own O not tracked");
            b[0] = WaterwayTile.X;
            Require(!tracker.TryObserve(b, Screen(seconds: 2)), "occupied tile changed without reset");
            Require(tracker.TryObserve(b, Screen(seconds: 3), verifiedBoardReset: true),
                "verified board reset rejected");
            Require(tracker.LastConfirmedOwnCorner is null, "reset retained prior own corner");
        });
        Test("board/X-center-invalidates-rule-hypothesis", () =>
        {
            var tracker = new WaterwayBoardTracker();
            var b = Empty(); b[4] = WaterwayTile.X;
            Require(tracker.TryObserve(b, Screen()), "X center reading failed");
            Require(tracker.CenterXObserved, "center X was suppressed");
            tracker.ResetForNewEntry();
            Require(!tracker.CenterXObserved && tracker.Board is null, "new run retained old tiles");
        });
        Test("camera/basis-maps-rotated-world-directions", () =>
        {
            var camera = new WaterwayCameraCalibration();
            Require(!camera.TryResolve(new Vector2(1, 1), out _), "uncalibrated input enabled");
            Require(camera.TryCalibrate(new Vector2(0, 1), new Vector2(-1, 0)),
                "rotated camera calibration failed");
            Require(camera.TryResolve(new Vector2(1, 1), out var keys) &&
                keys == (WaterwayKeys.W | WaterwayKeys.A), "world coordinates treated as screen axes");
            camera.Invalidate();
            Require(!camera.TryResolve(new Vector2(1, 1), out _), "camera change not honored");
        });
        Test("keys/held-until-changed-and-user-yield", () =>
        {
            var held = new WaterwayHeldKeyPlan();
            var first = held.Update(WaterwayKeys.W | WaterwayKeys.D, false, false, false);
            Require(first.Press == (WaterwayKeys.W | WaterwayKeys.D), "diagonal hold missing");
            var repeat = held.Update(WaterwayKeys.W | WaterwayKeys.D, false, false, false);
            Require(repeat.Press == WaterwayKeys.None && repeat.Release == WaterwayKeys.None,
                "repeated taps generated");
            var manual = held.Update(WaterwayKeys.W, true, false, false);
            Require(manual.Release == (WaterwayKeys.W | WaterwayKeys.D) &&
                held.Held == WaterwayKeys.None, "manual override failed");
            held.Update(WaterwayKeys.A, false, false, false);
            Require(held.Update(WaterwayKeys.A, false, true, false).Release == WaterwayKeys.A,
                "stale player position kept a key held");
        });
        Test("assist/H-toggle-only-on-observed-opposite", () =>
        {
            var gate = new WaterwayAssistGate();
            Require(!gate.ShouldTapH(WaterwayAssistState.Unknown, WaterwayAssistState.Off),
                "unknown assist triggered H");
            Require(gate.ShouldTapH(WaterwayAssistState.On, WaterwayAssistState.Off),
                "observed ON did not request OFF");
            Require(!gate.ShouldTapH(WaterwayAssistState.On, WaterwayAssistState.Off) &&
                gate.AwaitingConfirmation, "H repeatedly requested without acknowledgment");
            Require(!gate.ShouldTapH(WaterwayAssistState.Off, WaterwayAssistState.Off) &&
                !gate.AwaitingConfirmation, "OFF not acknowledged");
            Require(gate.ShouldTapH(WaterwayAssistState.Off, WaterwayAssistState.On),
                "auto skill ON not requested");
            Require(!gate.ShouldTapH(WaterwayAssistState.Unknown, WaterwayAssistState.On),
                "unknown triggered H while pending");
        });
        Console.WriteLine($"Waterway regressions passed: {_passed}");
    }
}
