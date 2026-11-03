namespace DungeonVisionBot;

// Feedback-based room-one experiment. Recognition supplies feet, never glyph
// centers as destinations. A dry run never predicts that the player has moved.
internal sealed class WaterwayMarkExecutor
{
    internal sealed record Observation(string Symbol, int PlayerX, int PlayerY,
        int PriestX, int PriestY, long CapturedAt, bool SharedRange,
        bool Success, bool Failure);
    internal sealed record Motion(string Key, double XPerTap, double YPerTap);
    internal sealed record Result(string State, string Reason, int Taps);

    internal static async Task<Result> RunAsync(
        Func<CancellationToken, Task<Observation?>> capture,
        Func<string, CancellationToken, Task> tap,
        IReadOnlyList<Motion> motions, bool inputAuthorized,
        Func<long> now, CancellationToken ct)
    {
        const int deadlineMs = 3000, maxTaps = 12;
        long started = now();
        string? symbol = null;
        int taps = 0;
        double? previousDistance = null;
        while (now() - started <= deadlineMs)
        {
            ct.ThrowIfCancellationRequested();
            var first = await capture(ct);
            var second = await capture(ct);
            ct.ThrowIfCancellationRequested();
            if (now() - started > deadlineMs)
                return new("Stopped", "표적 처리 시간 제한", taps);
            if (!Fresh(first, now()) || !Fresh(second, now()))
                return new("Stopped", "표적/발밑 위치 미확인 또는 오래된 화면", taps);
            if (first!.Failure || second!.Failure) return new("Failed", "실패 화면 단서", taps);
            if (first.Success && second.Success)
                return new("SuccessCue", "2프레임 성공 단서 관찰(자동파훼 인증 아님)", taps);
            if (!Valid(first, now()) || !Valid(second, now()))
                return new("Stopped", "표적/발밑 위치 미확인", taps);
            if (first!.Symbol != second!.Symbol || (symbol is not null && symbol != second.Symbol))
                return new("Stopped", "보스/표적 문양 변경", taps);
            if (Math.Abs(first.PriestX - second.PriestX) > 16 ||
                Math.Abs(first.PriestY - second.PriestY) > 16 ||
                Math.Abs(first.PlayerX - second.PlayerX) > 16 ||
                Math.Abs(first.PlayerY - second.PlayerY) > 16)
                return new("Stopped", "2프레임 위치 불일치/카메라 이동", taps);
            symbol = second.Symbol;

            double dx = second.PriestX - second.PlayerX;
            double dy = second.PriestY - second.PlayerY;
            double distance = dx * dx + dy * dy;
            if (!(first.SharedRange && second.SharedRange) && previousDistance is not null && distance >= previousDistance - 4)
                return new("Stopped", "이동 후 거리 감소 미확인", taps);
            if (first.SharedRange && second.SharedRange)
            {
                // In range: release all movement and observe the actual result.
                previousDistance = null;
                continue;
            }
            if (taps >= maxTaps) return new("Stopped", "이동 횟수 제한", taps);
            var move = motions.Where(m => m.Key is "W" or "A" or "S" or "D")
                .Where(m => double.IsFinite(m.XPerTap) && double.IsFinite(m.YPerTap)
                    && m.XPerTap * m.XPerTap + m.YPerTap * m.YPerTap is > 0 and <= 2500)
                .Select(m => (Motion: m, Distance: Math.Pow(dx - m.XPerTap, 2) + Math.Pow(dy - m.YPerTap, 2)))
                .OrderBy(m => m.Distance).FirstOrDefault();
            if (move.Motion is null || move.Distance >= distance - 4)
                return new("Stopped", "검증된 단일 탭 이동 경로 없음", taps);
            if (!inputAuthorized)
                return new("DryRun", "이동 후보=" + move.Motion.Key + "; 입력 차단, 실제 이동 미검증", taps);
            ct.ThrowIfCancellationRequested();
            await tap(move.Motion.Key, ct); // one bounded tap, then fresh two-frame feedback
            taps++;
            previousDistance = distance;
        }
        return new("Stopped", "표적 처리 시간 제한", taps);
    }

    private static bool Fresh(Observation? o, long now) => o is not null &&
        now >= o.CapturedAt && now - o.CapturedAt <= 500;
    private static bool Valid(Observation? o, long now) => Fresh(o, now) &&
        !string.IsNullOrWhiteSpace(o!.Symbol) &&
        o.PlayerX is >= 0 and < 800 && o.PlayerY is >= 0 and < 1000 &&
        o.PriestX is >= 0 and < 800 && o.PriestY is >= 0 and < 1000;
}
