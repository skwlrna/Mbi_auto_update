namespace FishingAutomation;

// Read-only Telegram projection. All values come from a UI-thread snapshot
// of the active F9 run and ONE fresh CLI works read; this class never inputs
// keys, changes scheduling, or rewrites the durable batch journal.
internal sealed record MultiAlteringTelegramItem(
    string FacilityName, string DisplayName,
    long ConfirmedQuantity, int TargetQuantity,
    int RegisteredWorks, int RequiredWorks,
    string MaterialState, long? EstimatedRemainingSeconds);

internal sealed record MultiAlteringTelegramSnapshot(
    IReadOnlyList<MultiAlteringTelegramItem> Items,
    string LastAction,
    DateTimeOffset LastActionAt);

internal static class MultiAlteringTelegramStatus
{
    private static string Short(string? text, int limit = 150)
    {
        string cleaned = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length > limit ? cleaned[..limit] + "…" : cleaned;
    }

    private static string Stage(string action)
    {
        if (action.Contains("오류", StringComparison.Ordinal) ||
            action.Contains("정체", StringComparison.Ordinal)) return "오류 / 상태 재확인";
        if (action.Contains("채집", StringComparison.Ordinal) ||
            action.Contains("재료 해결", StringComparison.Ordinal)) return "재료 준비 / 채집";
        if (action.Contains("수령", StringComparison.Ordinal) ||
            action.Contains("모두 받기", StringComparison.Ordinal)) return "완료품 수령 / 확인";
        if (action.Contains("이동", StringComparison.Ordinal) ||
            action.Contains("현장", StringComparison.Ordinal)) return "시설 이동 / 현장 확인";
        if (action.Contains("등록", StringComparison.Ordinal) ||
            action.Contains("배치 추가", StringComparison.Ordinal)) return "가공 작업 등록";
        if (action.Contains("대기", StringComparison.Ordinal) ||
            action.Contains("부분 완료", StringComparison.Ordinal)) return "가공 완료 대기";
        return "가공 관리 중";
    }

    internal static string Format(
        MultiAlteringTelegramSnapshot snapshot,
        IReadOnlyList<AlteringWork>? liveWorks,
        DateTimeOffset checkedAt,
        string? cliError)
    {
        var plans = snapshot.Items;
        var lines = new List<string>
        {
            "━━ F9 다중가공 상세 현황 ━━",
            $"현재 단계: {Stage(snapshot.LastAction)}",
            $"최근 동작: {Short(snapshot.LastAction, 175)}",
            $"동작 갱신: {(snapshot.LastActionAt == default ? "기록 없음" : snapshot.LastActionAt.ToLocalTime().ToString("HH:mm:ss"))}",
            $"목표 달성: {plans.Sum(x => x.ConfirmedQuantity):N0}/{plans.Sum(x => (long)x.TargetQuantity):N0}개 (수령 확인 기준)"
        };
        foreach (var item in plans)
        {
            bool finished = item.ConfirmedQuantity >= item.TargetQuantity;
            lines.Add(
                $"• {item.DisplayName}: {item.ConfirmedQuantity:N0}/{item.TargetQuantity:N0}개" +
                $" · 등록 {item.RegisteredWorks}/{item.RequiredWorks}회" +
                $" · 남은시간(추정) {AlteringStatusFormatter.FormatRemaining(item.EstimatedRemainingSeconds, finished)}");
            if (!string.IsNullOrWhiteSpace(item.MaterialState) &&
                item.MaterialState != "대기")
                lines.Add($"  재료/진행: {Short(item.MaterialState, 85)}");
        }

        if (liveWorks is null)
        {
            lines.Add($"시설 CLI: 실시간 조회 불가 ({Short(cliError, 100)}) · 작업 0건으로 취급하지 않음");
        }
        else
        {
            lines.Add($"시설 CLI 확인: {checkedAt.ToLocalTime():HH:mm:ss} (조회 시점 실제 상태)");
            foreach (var facility in plans.Select(x => x.FacilityName).Distinct(StringComparer.Ordinal))
            {
                var works = liveWorks.Where(x =>
                    string.Equals(x.FacilityName, facility, StringComparison.Ordinal)).ToArray();
                int done = works.Count(x => x.IsCompleted);
                int active = works.Count(x => x.State == "InProgress");
                int waiting = works.Length - done - active;
                lines.Add($"🏭 {facility.Replace(" 시설", "")}: 사용 {works.Length}/7칸 · 완료 {done} · 진행 {active} · 대기 {waiting}");

                if (works.Length == 0)
                {
                    lines.Add("  다음: 시설 등록 가능 여부를 관리자가 확인");
                    continue;
                }

                string composition = string.Join(", ", works.GroupBy(x => x.DisplayName)
                    .Select(g => $"{g.Key} {g.Count()}칸"));
                lines.Add($"  작업 구성: {Short(composition, 150)}");

                if (works.All(x => x.IsCompleted))
                    lines.Add("  다음: 시설 전체 완료 → 모두 받기 수령 조건 확인");
                else if (active > 0)
                {
                    long next = works.Where(x => x.State == "InProgress")
                        .Min(x => x.RemainingSeconds);
                    lines.Add(
                        $"  다음 완료 예상: {AlteringStatusFormatter.FormatRemaining(next, false)}" +
                        (done > 0 ? " · 일부 완료, 시설 전체 완료 전 수령 보류" : " · 가공 진행 대기"));
                }
                else
                    lines.Add("  다음: 대기 슬롯 / 가공 시작 여부 확인");
            }
        }

        // SendTextAsync uses 3900 chars for the WHOLE reply, including the
        // legacy /status header. Limit only the detail extension and explicitly
        // disclose omitted rows instead of Telegram silently slicing mid-line.
        var selected = new List<string>();
        int length = 0;
        foreach (string line in lines)
        {
            if (length + line.Length + 1 > 2600)
            {
                selected.Add("… 나머지 항목 생략 (메시지 길이 제한)");
                break;
            }
            selected.Add(line);
            length += line.Length + 1;
        }
        return string.Join("\n", selected);
    }
}
