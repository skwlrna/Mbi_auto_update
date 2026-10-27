namespace FishingAutomation;

// A read-only, UI-thread-captured report. No CLI calls, game inputs, or scheduler changes.
internal enum CraftingTelegramPhase { Pending, Active, Completed, Skipped, Stopped, Failed }

internal sealed record CraftingTelegramItem(
    string Category,
    string DisplayName,
    long ConfirmedQuantity,
    int TargetQuantity,
    int CompletedCrafts,
    int RequiredCrafts,
    CraftingTelegramPhase Phase,
    string Stage,
    string? Reason);

internal sealed record CraftingTelegramSnapshot(
    IReadOnlyList<CraftingTelegramItem> Items,
    int ActiveIndex,
    string LastAction,
    DateTimeOffset LastActionAt,
    bool Running,
    string? Error);

internal static class CraftingTelegramStatus
{
    private static string Short(string? value, int maxLength = 150)
    {
        string clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length > maxLength ? clean[..maxLength] + "…" : clean;
    }

    private static string Phase(CraftingTelegramPhase phase) => phase switch
    {
        CraftingTelegramPhase.Active => "▶ 진행 중",
        CraftingTelegramPhase.Completed => "✓ 완료",
        CraftingTelegramPhase.Skipped => "⚠ 재료 부족 건너뜀",
        CraftingTelegramPhase.Stopped => "⏹ 중지",
        CraftingTelegramPhase.Failed => "⛔ 오류",
        _ => "□ 대기"
    };

    internal static string Format(CraftingTelegramSnapshot snapshot)
    {
        var items = snapshot.Items;
        if (items.Count == 0) return "";

        int completed = items.Count(x => x.Phase == CraftingTelegramPhase.Completed);
        int skipped = items.Count(x => x.Phase == CraftingTelegramPhase.Skipped);
        long target = items.Sum(x => (long)x.TargetQuantity);
        long confirmed = items.Sum(x => Math.Clamp(x.ConfirmedQuantity, 0, x.TargetQuantity));
        bool validActive = snapshot.ActiveIndex >= 0 && snapshot.ActiveIndex < items.Count &&
            items[snapshot.ActiveIndex].Phase == CraftingTelegramPhase.Active;

        var lines = new List<string>
        {
            "━━ 제작 상세 현황 ━━",
            $"상태: {(snapshot.Running ? "실행 중" : "종료 / 마지막 기록")} · 총 {items.Count}종 · 완료 {completed}종 · 재료 부족 건너뜀 {skipped}종",
            validActive
                ? $"현재 순서: {snapshot.ActiveIndex + 1}/{items.Count} · {items[snapshot.ActiveIndex].DisplayName}"
                : $"현재 순서: {(snapshot.Running ? "다음 품목 전환 / 준비 중" : "진행 중인 품목 없음")}",
            $"목표 합계: {confirmed:N0}/{target:N0}개 (프로그램이 재고 증가로 확인한 수량)",
            $"최근 동작: {Short(snapshot.LastAction, 175)}",
            $"갱신: {(snapshot.LastActionAt == default ? "기록 없음" : snapshot.LastActionAt.ToLocalTime().ToString("HH:mm:ss"))}"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
            lines.Add($"정지/오류 사유: {Short(snapshot.Error, 200)}");

        foreach (var item in items)
        {
            long count = Math.Clamp(item.ConfirmedQuantity, 0, item.TargetQuantity);
            lines.Add(
                $"• [{item.Category}] {item.DisplayName}: {count:N0}/{item.TargetQuantity:N0}개" +
                $" · 제작 {item.CompletedCrafts}/{item.RequiredCrafts}회 · {Phase(item.Phase)}");
            if (item.Phase == CraftingTelegramPhase.Active)
            {
                lines.Add($"  현재 단계: {Short(item.Stage, 155)}");
                lines.Add($"  남은 목표: {Math.Max(0, item.TargetQuantity - count):N0}개 · 남은 제작 {Math.Max(0, item.RequiredCrafts - item.CompletedCrafts)}회(계획 기준)");
            }
            else if (item.Phase is CraftingTelegramPhase.Skipped or CraftingTelegramPhase.Failed or CraftingTelegramPhase.Stopped)
                lines.Add($"  사유: {Short(item.Reason ?? item.Stage, 180)}");
        }

        // TelegramNotifier limits the whole /status message to 3900 chars.
        // Reserve space for the common status header, never split a row mid-line.
        var result = new List<string>();
        int length = 0;
        foreach (string line in lines)
        {
            if (length + line.Length + 1 > 2900)
            {
                result.Add("… 나머지 품목 생략 (메시지 길이 제한)");
                break;
            }
            result.Add(line);
            length += line.Length + 1;
        }
        return string.Join("\n", result);
    }
}
