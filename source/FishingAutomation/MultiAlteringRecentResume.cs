using System.Text;
using System.Text.Json;

namespace FishingAutomation;

// Read-only verification is deliberately separate from the F9 fresh-start path.
// No UI code can declare a queue shrink or receipt as verified.
internal sealed record MultiAlteringRecentRun(
    string Directory, string BatchId, DateTimeOffset StartedAt,
    MultiAlteringBatch Batch)
{
    internal IReadOnlyList<AlteringPlan> Plans => Batch.Items.Select(s =>
        new AlteringPlan(s.FacilityName, s.DisplayName, s.TargetQuantity,
            s.ProducedPerWork, false, s.RecipeOrdinal)).ToArray();
}

internal sealed record MultiAlteringResumeLine(
    AlteringPlan Plan, long Confirmed, int LiveWorks, long RegisteredOutput,
    long AdditionalOutput);

internal static class MultiAlteringRecentResume
{
    private const string HiddenFileName = "recent-hidden.txt";

    internal static MultiAlteringRecentRun? Latest(string root)
    {
        string runs = Path.Combine(root, "fresh-runs");
        if (!System.IO.Directory.Exists(runs)) return null;
        var found = new List<MultiAlteringRecentRun>();
        foreach (string path in System.IO.Directory.EnumerateDirectories(runs, "f9-*"))
        {
            // A candidate is only an actual V3.1.80+ batch with a valid
            // persisted manifest. Never discover by modification time.
            string name = Path.GetFileName(path);
            if (name.Length != 35 || !name.StartsWith("f9-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[3..], "N", out _))
                continue;
            if (!File.Exists(Path.Combine(path, "batch.json"))) continue;
            var batch = MultiAlteringBatchStore.ReadManifestSnapshot(path);
            found.Add(new MultiAlteringRecentRun(
                path, batch.BatchId, batch.Items.Min(x => x.StartedAt), batch));
        }
        var latest = found.OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.BatchId, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null || latest.Batch.State != MultiAlteringBatchState.Active)
            return null; // Never fall back to an older unfinished batch.
        string hiddenPath = Path.Combine(root, HiddenFileName);
        if (File.Exists(hiddenPath) &&
            File.ReadAllText(hiddenPath, Encoding.UTF8).Trim() == latest.BatchId)
            return null;
        return latest;
    }

    // Must be called while holding the root MultiAlteringBatchStore lease.
    // Removing a resume target never alters a manifest, game work or inventory.
    internal static void HideLatest(string root, string expectedBatchId)
    {
        var latest = Latest(root);
        if (latest is null || latest.BatchId != expectedBatchId)
            throw new InvalidOperationException(
                "초기화 대상이 변경되었습니다. 기록을 다시 조회하세요.");
        string path = Path.Combine(root, HiddenFileName);
        string temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create,
                FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(expectedBatchId);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new IOException(
                "최근 작업 초기화 표시 저장 실패 · 기존 기록 보존", ex);
        }
    }

    internal static async Task<IReadOnlyList<MultiAlteringResumeLine>> VerifyAsync(
        MultiAlteringRecentRun selected, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Re-read the manifest each time. No cached modal snapshot can authorize
        // a later registration if another run has completed or reset it.
        var actual = MultiAlteringBatchStore.ReadManifestSnapshot(selected.Directory);
        if (actual.BatchId != selected.BatchId ||
            actual.State != MultiAlteringBatchState.Active ||
            actual.Identity != identity)
            throw new InvalidOperationException(
                "이어하기 배치 상태 또는 캐릭터 정보가 변경됐습니다 · 등록 차단");
        if (!identity.HasDurableMultiIdentity)
            throw new InvalidOperationException(
                "이어하기에는 캐릭터 고유 식별 정보가 필요합니다. 서버명만 일치하는 1캐릭터 모드에서 다른 캐릭터의 기록을 적용할 수 없어 자동 재개를 차단합니다.");
        if (actual.PreparedConsumption is not null ||
            actual.Items.Any(s => s.MultiState == MultiAlteringItemState.RecoveryRequired ||
                s.PendingRegistration || s.PendingConsumptionTransactionId is not null))
            throw new InvalidOperationException(
                "등록·수령·중간재료 소비 확정 중 중단된 기록입니다 · RecoveryRequired · 수동 검증 전 중복 등록 금지");
        var plans = actual.Items.Select(s => new AlteringPlan(
            s.FacilityName, s.DisplayName, s.TargetQuantity,
            s.ProducedPerWork, false, s.RecipeOrdinal)).ToArray();
        if (plans.Select(p => (p.FacilityName, p.OutputName))
            .Distinct().Count() != plans.Length)
            throw new InvalidOperationException(
                "같은 시설의 동일 출력품이 여러 제법에 있어 저장된 작업 소유권을 분리할 수 없습니다.");

        var before = await data.WorksAsync(ct);
        var result = new List<MultiAlteringResumeLine>();
        foreach (var session in actual.Items)
        {
            ct.ThrowIfCancellationRequested();
            var plan = plans.Single(p => p.FacilityName == session.FacilityName &&
                p.DisplayName == session.DisplayName && p.RecipeOrdinal == session.RecipeOrdinal);
            if (session.MultiState == MultiAlteringItemState.Completed)
            {
                result.Add(new(plan, plan.TargetQuantity, 0, 0, 0));
                continue;
            }
            if (session.InitialExistingWorks != 0)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 시작 전부터 있던 동일 품목 작업의 소유권을 이어하기에서 구분할 수 없습니다 · 안전 정지");
            long current = checked(await data.ItemCountAsync(plan.OutputName, ct) +
                session.CreditedInternalConsumptionQuantity);
            if (current < session.LastObservedOutputQuantity ||
                current < session.BaselineQuantity)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 저장 이후 완성품 재고가 감소했거나 소비 기록과 불일치합니다 · 자동 복원 차단");
            long earned = current - session.BaselineQuantity;
            long confirmed = Math.Clamp(earned, 0, plan.TargetQuantity);
            int completedEquivalent = (int)Math.Min(session.QueuedWorks,
                earned / plan.ProducedPerWork);
            var facility = before.Where(x => x.FacilityName == plan.FacilityName).ToArray();
            int live = facility.Count(x =>
                x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName);
            int expectedLive = session.QueuedWorks - completedEquivalent;
            if (live != expectedLive)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 저장 등록 {session.QueuedWorks}건 · 확인된 완성 작업 {completedEquivalent}건 · " +
                    $"게임 대기열 {live}건. 등록/수령/수동 취소가 불명확하여 자동 이어하기 차단");
            if (facility.Any(x => !plans.Any(p =>
                p.FacilityName == x.FacilityName &&
                (p.DisplayName == x.DisplayName || p.OutputName == x.DisplayName))))
                throw new InvalidOperationException(
                    $"{plan.FacilityName}: 저장 목표 밖의 게임 작업이 남아 시설 소유권이 불명확합니다.");
            long registeredOutput = checked((long)live * plan.ProducedPerWork);
            long additional = Math.Max(0, plan.TargetQuantity - confirmed - registeredOutput);
            result.Add(new(plan, confirmed, live, registeredOutput, additional));
        }
        var after = await data.WorksAsync(ct);
        static string Shape(IEnumerable<AlteringWork> works) =>
            string.Join("|", works.GroupBy(x => (
                x.FacilityName, x.DisplayName, x.IsCompleted))
                .OrderBy(x => x.Key.FacilityName, StringComparer.Ordinal)
                .ThenBy(x => x.Key.DisplayName, StringComparer.Ordinal)
                .ThenBy(x => x.Key.IsCompleted)
                .Select(x => $"{x.Key.FacilityName}/{x.Key.DisplayName}/{x.Key.IsCompleted}:{x.Count()}"));
        if (Shape(before) != Shape(after))
            throw new InvalidOperationException(
                "이어하기 검증 도중 실제 시설 대기열이 변경됐습니다 · 다시 검증하세요.");
        if (MultiAlteringBatchStore.ReadManifestSnapshot(selected.Directory).BatchId !=
            selected.BatchId)
            throw new InvalidOperationException("이어하기 기록이 변경됐습니다.");
        return result;
    }
}
