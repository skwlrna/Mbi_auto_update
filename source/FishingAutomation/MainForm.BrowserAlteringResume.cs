using System.Text.Json;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed record BrowserResumeTicket(string Id, string BatchId, string? Realm,
        bool Verified, bool SameCharacter, long IssuedAt);
    private BrowserResumeTicket? _browserResumeTicket;

    private static bool IsBrowserResumeTicketFresh(BrowserResumeTicket ticket) =>
        Environment.TickCount64 - ticket.IssuedAt < 120_000;

    private static BrowserResumeTicket IssueBrowserResumeTicket(string batchId, string? realm,
        bool verified, bool sameCharacter) =>
        new(Guid.NewGuid().ToString("N"), batchId, realm, verified, sameCharacter, Environment.TickCount64);

    private async Task<string> VerifyBrowserAlteringResumeAsync(string arguments, CancellationToken token)
    {
        if (!CanResetBrowserPreviousAlteringBatch())
            return "PC 작업 종료 후 이전 배치를 검증하세요.";

        string[] values = arguments.Split(' ', 2);
        var prior = _browserResumeTicket;
        _browserResumeTicket = null;
        _resumeUiBusy = true;
        try
        {
            string root = BrowserAlteringBatchRoot;
            var recent = MultiAlteringRecentResume.Latest(root);
            if (recent is null || recent.BatchId != values[0])
                return "이어하기 대상이 변경되었습니다. 이전 배치를 다시 확인하세요.";

            await CliAutomationGuards.EnsureCapabilitiesAsync(_cli,
                new[] { "get_my_info", "get_altering_works", "get_items", "get_inventory" }, token);
            var identity = await CliIdentityGuard.CaptureForMultiAlteringAsync(
                _cli, token, allowLimitedFreshTest: true);
            bool sameCharacter = !identity.Baseline.HasDurableMultiIdentity;
            if (sameCharacter)
            {
                string? realm = identity.Baseline.RealmName;
                if (string.IsNullOrWhiteSpace(realm))
                    return "1캐릭터 이어하기 서버 확인 실패 · 기록 보존";
                if (values.Length == 1)
                {
                    var consent = IssueBrowserResumeTicket(recent.BatchId, realm, verified: false, sameCharacter: true);
                    _browserResumeTicket = consent;
                    return JsonSerializer.Serialize(new { consent = consent.Id, realm });
                }
                if (prior is not { Verified: false } || prior.Id != values[1] ||
                    prior.BatchId != recent.BatchId || prior.Realm != realm || !IsBrowserResumeTicketFresh(prior))
                    return "캐릭터 확인이 만료되었거나 서버가 변경되었습니다. 다시 검증하세요.";
            }
            else if (values.Length != 1)
                return "캐릭터 확인 정보가 변경되었습니다. 다시 검증하세요.";

            string report = await VerifyRecentAlteringAsync(root, recent, identity.Baseline, sameCharacter, token);
            token.ThrowIfCancellationRequested();
            var verified = IssueBrowserResumeTicket(recent.BatchId, identity.Baseline.RealmName,
                verified: true, sameCharacter: sameCharacter);
            _browserResumeTicket = verified;
            return JsonSerializer.Serialize(new { ticket = verified.Id, report });
        }
        catch (InvalidOperationException ex)
        {
            _log.Write("[브라우저 이어하기] 검증 실패: " + ex.Message);
            return "이어하기 검증 실패 · " + ex.Message;
        }
        finally { _resumeUiBusy = false; }
    }

    private async Task<string> StartBrowserAlteringResumeAsync(string ticketId, CancellationToken token)
    {
        if (!CanResetBrowserPreviousAlteringBatch())
            return "다른 PC 작업이 진행 중입니다. 종료 후 다시 검증하세요.";
        var ticket = _browserResumeTicket;
        if (ticket is not { Verified: true } || ticket.Id != ticketId || !IsBrowserResumeTicketFresh(ticket))
            return "이어하기 검증이 만료되었습니다. 현재 상태를 다시 검증하세요.";
        _browserResumeTicket = null;
        _resumeUiBusy = true;
        try
        {
            var recent = MultiAlteringRecentResume.Latest(BrowserAlteringBatchRoot);
            if (recent is null || recent.BatchId != ticket.BatchId)
                return "이어하기 대상이 변경되었습니다. 다시 검증하세요.";
            var failure = await WindowTools.ActivateGameForRemoteStartAsync(token);
            if (failure is not null) return failure;
            token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing) return "PC 연결이 종료되었습니다.";
            if (!RunAutomationStartPreflight("가공")) return _statusValue.Text;
            if (!WindowTools.IsRequiredGameWindow(NativeMethods.GetForegroundWindow()))
                return WindowTools.RemoteFocusError;

            // The common start path rechecks identity, pending plans, and the live queue
            // under the batch lease before it registers or receives any game work.
            _resumeUiBusy = false;
            ObserveStartTask(StartMultiAlteringAsync(recent.Plans, recent.Directory,
                recent.BatchId, ticket.SameCharacter), "가공");
            return "이어하기 시작 요청 접수 · 캐릭터와 가공 대기열을 다시 검증합니다.";
        }
        finally { _resumeUiBusy = false; }
    }
}
