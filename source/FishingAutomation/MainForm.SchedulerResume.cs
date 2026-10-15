using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    // Only an explicit user choice can open an existing durable multi-altering
    // ledger. This is a scheduler entry point into the SAME engine and verifier
    // used by the production page's manual resume button.
    private sealed record SchedulerAlteringResumeSelection(
        IReadOnlyList<AlteringPlan> Plans, string Directory, string BatchId,
        bool ConfirmedSingleCharacter);

    private async Task<(bool Cancelled, SchedulerAlteringResumeSelection? Resume)>
        ChooseSchedulerMultiAlteringAsync(
            IReadOnlyList<AlteringPlan> selectedPlans, bool craftingSelected,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MabiAuto", "multi-altering");

        MultiAlteringRecentRun? recent;
        // The root lease ensures the latest manifest is read consistently.
        using (var lease = new MultiAlteringBatchStore(root))
            recent = MultiAlteringRecentResume.Latest(root);

        // No unfinished recent run: preserve the scheduler's usual fresh path.
        if (recent is null) return (false, null);

        var savedPlans = recent.Plans;
        bool sameSelection = selectedPlans.Select(p =>
                MultiAlteringBatchStore.Key(p) + "\u001f" + p.TargetQuantity)
            .SequenceEqual(savedPlans.Select(p =>
                MultiAlteringBatchStore.Key(p) + "\u001f" + p.TargetQuantity),
                StringComparer.Ordinal);
        string listDifference = sameSelection
            ? "현재 가공 상세의 작업 목록과 저장 기록이 일치합니다."
            : "현재 가공 상세 목록과 저장 기록이 다릅니다.\r\n" +
              "이어하기를 선택하면 아래 저장 목록을 사용합니다.";
        string savedSummary = string.Join(Environment.NewLine,
            savedPlans.Select(p => p.DisplayName + " · 목표 " +
                p.TargetQuantity.ToString("N0") + "개"));
        using (var prompt = MakeResumeDialog(
            "스케줄러 · 다중가공 시작", "미완료 다중가공 기록 발견",
            "작업 ID: " + recent.BatchId[..8] + Environment.NewLine +
            listDifference + Environment.NewLine + Environment.NewLine +
            savedSummary + Environment.NewLine + Environment.NewLine +
            (craftingSelected
                ? "제작이 체크되어 있어 제작을 먼저 다시 실행한 후 가공을 이어합니다."
                : "가공 단계부터 이어하며 설정된 어비스 병행도 유지합니다.") +
            Environment.NewLine +
            "신규 시작은 완료 수량을 복원하지 않습니다. 중복 등록에 주의하세요.",
            ("저장 작업 이어하기", DialogResult.OK),
            ("신규 작업 시작", DialogResult.Yes),
            ("취소", DialogResult.Cancel)))
        {
            var choice = prompt.ShowDialog(this);
            token.ThrowIfCancellationRequested();
            if (choice == DialogResult.Yes)
            {
                _log.Write("[스케줄러] 사용자가 신규 시작 선택 · 이전 기록 보존");
                return (false, null);
            }
            if (choice != DialogResult.OK) return (true, null);
        }

        // Read-only preflight. Never create a ledger or touch the game before
        // selection, identity consent, queue/quantity verification and confirm.
        // These commands only query CLI capabilities / identity. Retry once
        // for a recognized transport failure; never replay game inputs.
        await SchedulerReadOnlyRetry.RunAsync(
            ct => CliAutomationGuards.EnsureCapabilitiesAsync(_cli,
                new[] { "get_my_info", "get_altering_works", "get_items", "get_inventory" },
                ct), token, _log.Write);
        var identity = await SchedulerReadOnlyRetry.RunAsync(
            ct => CliIdentityGuard.CaptureForMultiAlteringAsync(
                _cli, ct, allowLimitedFreshTest: true), token, _log.Write);
        token.ThrowIfCancellationRequested();

        bool confirmedSingleCharacter = false;
        if (!identity.Baseline.HasDurableMultiIdentity)
        {
            if (string.IsNullOrWhiteSpace(identity.Baseline.RealmName))
                throw new InvalidOperationException(
                    "이어하기 서버 확인 실패 · 기존 기록 보존");
            using var consent = MakeResumeDialog(
                "스케줄러 · 캐릭터 확인", "같은 캐릭터 사용 확인",
                "현재 서버: " + identity.Baseline.RealmName +
                Environment.NewLine +
                "이 서버의 같은 캐릭터 하나로만 대량가공하셨습니까?" +
                Environment.NewLine +
                "서버명만으로 다른 캐릭터를 구별할 수 없습니다." +
                Environment.NewLine +
                "다른 캐릭터였다면 [아니요]를 선택하세요.",
                ("예, 같은 캐릭터", DialogResult.Yes),
                ("아니요", DialogResult.Cancel));
            if (consent.ShowDialog(this) != DialogResult.Yes)
                return (true, null);
            token.ThrowIfCancellationRequested();
            confirmedSingleCharacter = true;
        }

        IReadOnlyList<MultiAlteringResumeLine> verified;
        using (var lease = new MultiAlteringBatchStore(root))
        {
            var latest = MultiAlteringRecentResume.Latest(root);
            if (latest is null || latest.BatchId != recent.BatchId ||
                !string.Equals(Path.GetFullPath(latest.Directory),
                    Path.GetFullPath(recent.Directory),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "최근 이어하기 대상이 변경됐습니다 · 시작 차단");
            verified = await MultiAlteringRecentResume.VerifyAsync(
                latest, identity.Baseline, new AlteringCliData(_cli), token,
                allowSingleCharacter: confirmedSingleCharacter);
        }
        token.ThrowIfCancellationRequested();

        string report = string.Join(Environment.NewLine, verified.Select(line =>
            line.Plan.DisplayName + " · 목표 " +
            line.Plan.TargetQuantity.ToString("N0") +
            "개 / 완성 확정 " + line.Confirmed.ToString("N0") +
            "개 / 등록 중 " + line.RegisteredOutput.ToString("N0") +
            "개 상당 / 신규 필요 " + line.AdditionalOutput.ToString("N0") +
            "개 상당"));
        using (var confirm = MakeResumeDialog(
            "스케줄러 · 이어하기 검증", "현재 상태 검증 통과",
            report + Environment.NewLine + Environment.NewLine +
            "저장 목표/완료 수량을 유지하며, 가공 시작 직전에 " +
            "게임 대기열·캐릭터·장부를 다시 검증합니다." +
            Environment.NewLine +
            (craftingSelected
                ? "제작 단계는 먼저 다시 실행합니다."
                : "가공 단계부터 실행합니다."),
            ("검증 후 이어하기", DialogResult.OK),
            ("취소", DialogResult.Cancel)))
        {
            if (confirm.ShowDialog(this) != DialogResult.OK)
                return (true, null);
        }
        token.ThrowIfCancellationRequested();
        _log.Write("[스케줄러] 저장 다중가공 이어하기 사용자 확인 · " +
                   recent.BatchId + " · 등록 전 재검증 예정");
        return (false, new SchedulerAlteringResumeSelection(
            savedPlans, recent.Directory, recent.BatchId,
            confirmedSingleCharacter));
    }
}
