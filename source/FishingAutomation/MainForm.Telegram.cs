namespace FishingAutomation;

public sealed partial class MainForm
{
    private async Task<string> HandleTelegramCommandAsync(string command)
    {
        switch (command)
        {
            case "/status":
            case "상태":
                string summary = GetRemoteStatus();
                string detail = await GetMultiAlteringTelegramDetailsAsync();
                return string.IsNullOrWhiteSpace(detail)
                    ? summary
                    : summary + "\n\n" + detail;

            case "/item":
                return LootStats.GetTelegramReport();

            case "/itemreset":
                return LootStats.ResetLifetime();

            case "/stop":
                Ui(StopSelected);
                await Task.Delay(300);
                return "⏹ MABI AUTO 정지 요청 완료\n" + GetRemoteStatus();

            case "/restart":
                Ui(StopSelected);
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (!AnyRunning) break;
                }
                if (AnyRunning) return "⚠️ 기존 작업이 아직 종료되지 않아 재시작하지 않았습니다.";
                Ui(StartSelected);
                await Task.Delay(500);
                return "🔄 MABI AUTO 재시작 요청 완료\n" + GetRemoteStatus();

            default:
                return "지원하지 않는 명령입니다. /help 를 입력하세요.";
        }
    }

    private string GetRemoteStatus()
    {
        if (InvokeRequired)
            return (string)Invoke(new Func<string>(GetRemoteStatus));

        string mode = _activeMode ?? SelectedMode;
        string running = AnyRunning ? "실행 중" : "대기 중";
        string dungeon = mode == "어비스" ? $"\n던전: {SelectedAbyssDungeon}" : "";
        string started = mode is "던전" or "어비스" or "가공"
            ? _dungeonStartedAt?.ToString("HH:mm:ss") ?? "—"
            : _fishingStartedAt?.ToString("HH:mm:ss") ?? "—";
        string altering = mode == "가공" ? GetAlteringRemoteStatus() : "";
        string alteringBlock = string.IsNullOrWhiteSpace(altering) ? "" : $"\n{altering}";

        return $"MABI AUTO {UpdateManager.CurrentVersion}\n상태: {running}\n모드: {mode}{dungeon}\n화면 상태: {_statusValue.Text}{alteringBlock}\n시작: {started}\n성공: {_successValue.Text} / 진행: {_roundValue.Text}\n실패: {_failureValue.Text}";
    }
}
