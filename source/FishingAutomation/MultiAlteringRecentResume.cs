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

    internal sealed record OwnedWorkReconciliation(
        long TotalReceivedWorks, long OwnReceivedOutput,
        long OwnRegisteredOutstandingOutput, long ExpectedLive);

    // Evidence only. Does not update a ledger, assume stable work IDs or send
    // a game input. Pre-existing same-item jobs are never credited to F9.
    internal static OwnedWorkReconciliation ReconcileOwnedWorks(
        AlteringSessionState session, long gainedOutput, int liveWorks)
    {
        if (session.ProducedPerWork <= 0 || session.InitialExistingWorks < 0 ||
            session.QueuedWorks < 0 || liveWorks < 0 || gainedOutput < 0)
            throw new InvalidOperationException(
                $"{session.DisplayName}: 이어하기 가공 장부 값이 잘못됐습니다.");
        long total = checked((long)session.InitialExistingWorks + session.QueuedWorks);
        if (session.ReceiptTrackingEnabled)
        {
            // Receipt confirmation requires BOTH the saved pre-input queue
            // snapshot and the proven post-input facility drain. Inventory
            // surplus cannot create any additional receipt credits.
            long received = session.ConfirmedReceivedWorks;
            long expected = total - received;
            if (received < 0 || received > total || liveWorks != expected)
                throw new InvalidOperationException(
                    $"{session.DisplayName}: 수령 확정 {received}건 / 전체 {total}건 / " +
                    $"예상 대기열 {expected}건 / 실제 {liveWorks}건 · 게임 대기열 불일치");
            long minimumOutput = checked(received * session.ProducedPerWork);
            if (gainedOutput < minimumOutput)
                throw new InvalidOperationException(
                    $"{session.DisplayName}: 수령 확정 {received}건에 비해 완성품 증가가 부족합니다 · 안전 정지");
            long ownReceived = Math.Max(0, received - session.InitialExistingWorks);
            long ownPending = session.QueuedWorks - ownReceived;
            return new(received, checked(ownReceived * session.ProducedPerWork),
                checked(ownPending * session.ProducedPerWork), expected);
        }
        // Older releases had no pre-input receipt journal. Keep the older
        // EXACT stock/queue equality check; do not reinterpret uncertain
        // historical inventory gains as verified receipts.
        if (gainedOutput % session.ProducedPerWork != 0 ||
            gainedOutput > checked(total * session.ProducedPerWork))
            throw new InvalidOperationException(
                $"{session.DisplayName}: 저장된 기존 작업과 이번 등록 작업으로 증명할 수 없는 완성 수량 · 안전 정지");

        long received = gainedOutput / session.ProducedPerWork;
        long expectedLive = total - received;
        if (liveWorks != expectedLive)
            throw new InvalidOperationException(
                $"{session.DisplayName}: 기존 {session.InitialExistingWorks}건 + 이번 등록 {session.QueuedWorks}건 − " +
                $"수령 상당 {received}건 = 예상 {expectedLive}건, 실제 {liveWorks}건. " +
                "게임 대기열 작업 소유권/수령 불일치로 이어하기 차단");

        // Credit older jobs FIRST, never attribute their production to this
        // saved goal. Q remains the durable number of confirmed new F9 inputs;
        // re-registering Q because old and new jobs are visually identical
        // would duplicate production.
        long newlyReceivedWorks = Math.Max(0, received - session.InitialExistingWorks);
        long outstandingNewWorks = session.QueuedWorks - newlyReceivedWorks;
        if (outstandingNewWorks < 0)
            throw new InvalidOperationException(
                $"{session.DisplayName}: 이번 작업의 수령 수량이 등록 기록을 초과했습니다.");
        return new(received, checked(newlyReceivedWorks * session.ProducedPerWork),
            checked(outstandingNewWorks * session.ProducedPerWork), expectedLive);
    }

    internal static async Task<IReadOnlyList<MultiAlteringResumeLine>> VerifyAsync(
        MultiAlteringRecentRun selected, CliIdentityContext identity,
        IAlteringData data, CancellationToken ct,
        bool allowSingleCharacter = false)
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
        // Explicit one-character mode accepts an EXACT match to the saved,
        // repeatedly sampled realm-only identity. A realm name is NOT a
        // character identifier: the UI must obtain explicit user confirmation
        // and all queue, receipt, inventory and journal guards remain active.
        if (!identity.HasDurableMultiIdentity &&
            (!allowSingleCharacter || string.IsNullOrWhiteSpace(identity.RealmName) ||
             !string.IsNullOrWhiteSpace(identity.CharacterId) ||
             !string.IsNullOrWhiteSpace(identity.CharacterName) ||
             !string.IsNullOrWhiteSpace(identity.AccountCode)))
            throw new InvalidOperationException(
                "서버명만 확인된 1캐릭터 이어하기는 사용자 확인이 필요합니다 · 기존 작업 기록 보존 · 입력 차단");
        if (actual.PreparedConsumption is not null ||
            actual.PendingReceipt is not null ||
            actual.Items.Any(s => s.MultiState == MultiAlteringItemState.RecoveryRequired ||
                s.PendingRegistration || s.PendingConsumptionTransactionId is not null))
            throw new InvalidOperationException(
                "등록·수령·중간재료 소비 확정 중 중단된 기록입니다 · RecoveryRequired · 수동 검증 전 중복 등록 금지");
        var plans = actual.Items.Select(s => new AlteringPlan(
            s.FacilityName, s.DisplayName, s.TargetQuantity,
            s.ProducedPerWork, false, s.RecipeOrdinal)).ToArray();
        if (plans.Select(p => p.OutputName)
            .Distinct(StringComparer.Ordinal).Count() != plans.Length)
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
            long current = checked(await data.ItemCountAsync(plan.OutputName, ct) +
                session.CreditedInternalConsumptionQuantity);
            if (current < session.LastObservedOutputQuantity ||
                current < session.BaselineQuantity)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 저장 이후 완성품 재고가 감소했거나 소비 기록과 불일치합니다 · 자동 복원 차단");
            var facility = before.Where(x => x.FacilityName == plan.FacilityName).ToArray();
            int live = facility.Count(x =>
                x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName);
            if (facility.Length > 7)
                throw new InvalidOperationException(
                    $"{plan.FacilityName}: 시설 대기열이 7칸을 초과하여 검증할 수 없습니다.");
            // Old F9 jobs are NOT part of this batch's goal. The durable
            // initial-existing counter is a separate provenance bucket.
            // Work IDs are unavailable: only a fully conserved sum of
            // collected output and remaining live jobs can authorize resume.
            var accounted = ReconcileOwnedWorks(
                session, checked(current - session.BaselineQuantity), live);
            long confirmed = Math.Clamp(accounted.OwnReceivedOutput, 0, plan.TargetQuantity);
            long registeredOutput = accounted.OwnRegisteredOutstandingOutput;
            long additional = Math.Max(0, plan.TargetQuantity - confirmed - registeredOutput);
            if (session.QueuedWorks == session.RequiredWorks && live == 0 &&
                session.MultiState != MultiAlteringItemState.Completed)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 전량 등록 작업은 사라졌지만 완료 수령 확정 기록이 없습니다 · 중복 수령/등록 차단");
            if (live != accounted.ExpectedLive)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}: 저장 기존 {session.InitialExistingWorks}건 · 이번 등록 {session.QueuedWorks}건 · " +
                    $"완성/수령 상당 {accounted.TotalReceivedWorks}건 · 실제 대기열 {live}건. " +
                    "수동 취소·타인 등록·수령 불일치 가능성 · 자동 이어하기 차단");
            if (facility.Any(x => !plans.Any(p =>
                p.FacilityName == x.FacilityName &&
                (p.DisplayName == x.DisplayName || p.OutputName == x.DisplayName))))
                throw new InvalidOperationException(
                    $"{plan.FacilityName}: 저장 목표 밖의 게임 작업이 남아 시설 소유권이 불명확합니다.");
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
        var reloaded = MultiAlteringBatchStore.ReadManifestSnapshot(selected.Directory);
        if (reloaded.BatchId != selected.BatchId ||
            reloaded.State != MultiAlteringBatchState.Active ||
            reloaded.Identity != actual.Identity ||
            reloaded.Items.Length != actual.Items.Length ||
            reloaded.PreparedConsumption is not null ||
            reloaded.PendingReceipt is not null ||
            reloaded.Items.Any(x => x.PendingRegistration ||
                x.PendingConsumptionTransactionId is not null ||
                x.MultiState == MultiAlteringItemState.RecoveryRequired))
            throw new InvalidOperationException(
                "이어하기 검증 도중 저장 기록이 변경됐습니다 · 다시 검증하세요.");
        return result;
    }
}
