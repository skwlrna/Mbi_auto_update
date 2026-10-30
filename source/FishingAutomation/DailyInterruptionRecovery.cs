using System.Drawing;

namespace FishingAutomation;

internal enum DailyInterruptionKind { None, StellaPick, Notice, NpcGreeting }
internal sealed record DailyInterruptionObservation(
    DailyInterruptionKind Kind, Rectangle Anchor, Point? Close = null,
    Point? Advance = null, Point? Skip = null);

// No once-a-day suppression: 06:00 resets eligibility, not our observation loop.
// This controller owns modal inputs only. It cannot register, collect, travel,
// or restart a production operation. Callers must use fresh state afterwards.
internal sealed class DailyInterruptionRecovery(
    Func<CancellationToken, Task<DailyInterruptionObservation>> observe,
    Action<CancellationToken> escape,
    Action<Point, CancellationToken> click,
    Func<int, CancellationToken, Task> delay,
    Action<string> log)
{
    internal async Task<bool> RecoverAsync(CancellationToken ct)
    {
        bool handled = false;
        for (int episode = 0; episode < 8; episode++)
        {
            ct.ThrowIfCancellationRequested();
            var first = await observe(ct).ConfigureAwait(false);
            if (first.Kind == DailyInterruptionKind.None) return handled;
            await delay(180, ct).ConfigureAwait(false);
            var current = await observe(ct).ConfigureAwait(false);
            if (!Same(first, current)) continue;

            handled = true;
            log($"[일일 화면] {current.Kind} 확인 · 기존 작업 입력 일시 보류");
            if (current.Kind != DailyInterruptionKind.NpcGreeting)
            {
                ct.ThrowIfCancellationRequested();
                escape(ct); // exactly once, then inspect before considering X
                await delay(450, ct).ConfigureAwait(false);
                var after = await observe(ct).ConfigureAwait(false);
                if (Same(current, after))
                {
                    await delay(180, ct).ConfigureAwait(false);
                    var fresh = await observe(ct).ConfigureAwait(false);
                    if (Same(after, fresh) && StableTarget(after.Close, fresh.Close))
                    {
                        ct.ThrowIfCancellationRequested();
                        click(fresh.Close!.Value, ct);
                        await delay(450, ct).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // Click only a recognized greeting bubble. Skip wording is
                // deliberately not assumed to be exactly '대화 넘기기'.
                for (int step = 0; step < 12; step++)
                {
                    var a = await observe(ct).ConfigureAwait(false);
                    if (a.Kind != DailyInterruptionKind.NpcGreeting) break;
                    await delay(180, ct).ConfigureAwait(false);
                    var b = await observe(ct).ConfigureAwait(false);
                    if (!Same(a, b)) continue;
                    Point? target = StableTarget(a.Skip, b.Skip) ? b.Skip :
                        StableTarget(a.Advance, b.Advance) ? b.Advance : null;
                    if (target is null) break;
                    ct.ThrowIfCancellationRequested();
                    click(target.Value, ct);
                    log(b.Skip is not null ? "[일일 화면] NPC 건너뛰기 클릭" :
                        "[일일 화면] NPC 인사 대화 진행 1회");
                    await delay(650, ct).ConfigureAwait(false);
                }
            }

            // Two absent observations are required. A different known modal
            // is handled by the next episode, without returning control/input.
            bool cleared = false;
            for (int poll = 0; poll < 12; poll++)
            {
                await delay(250, ct).ConfigureAwait(false);
                var a = await observe(ct).ConfigureAwait(false);
                if (a.Kind != current.Kind)
                {
                    await delay(180, ct).ConfigureAwait(false);
                    var b = await observe(ct).ConfigureAwait(false);
                    if (b.Kind != current.Kind) { cleared = true; break; }
                }
            }
            if (!cleared)
                throw new InvalidOperationException(
                    $"일일 화면 {current.Kind} 닫힘 미확인 · 반복 ESC/무차별 클릭 없이 진단 정지");
            log($"[일일 화면] {current.Kind} 종료 확인 · 새 화면/CLI 재검증 후 기존 단계 계속");
        }
        throw new InvalidOperationException("일일 화면 연속 처리 한도 초과 · 진단 정지");
    }

    private static bool Same(DailyInterruptionObservation a, DailyInterruptionObservation b)
        => a.Kind != DailyInterruptionKind.None && a.Kind == b.Kind &&
           Math.Abs(a.Anchor.X - b.Anchor.X) <= 12 &&
           Math.Abs(a.Anchor.Y - b.Anchor.Y) <= 12;

    private static bool StableTarget(Point? a, Point? b)
        => a is not null && b is not null &&
           Math.Abs(a.Value.X - b.Value.X) <= 10 &&
           Math.Abs(a.Value.Y - b.Value.Y) <= 10;
}
