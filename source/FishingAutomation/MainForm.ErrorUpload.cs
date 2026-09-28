namespace FishingAutomation;

public sealed partial class MainForm
{
    private Task SendRuntimeAlertAsync(string title, string detail, bool screenshot = true)
    {
        string mode = _activeMode ?? SelectedMode;
        string selectedTarget = mode switch
        {
            "어비스" => SelectedAbyssDungeon,
            "던전" => SelectedDungeonDestination,
            _ => ""
        };
        var report = new RuntimeErrorReport
        {
            Time = DateTimeOffset.Now,
            Version = UpdateManager.CurrentVersion,
            Title = title,
            Detail = detail,
            Mode = mode,
            SelectedTarget = selectedTarget,
            ScreenStatus = _statusValue.Text
        };

        return Task.WhenAll(
            _notifier.SendAlertAsync(title, detail, screenshot),
            _errorUploader.UploadAlertAsync(report, screenshot));
    }

    private async void ShowErrorUploadSettings()
    {
        var current = _errorUploader.Settings;
        using var dialog = new ErrorUploadSettingsDialog(
            current,
            _errorUploader.HasStoredToken,
            (repo, branch, token) => _errorUploader.TestConnectionAsync(repo, branch, token));

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var settings = new ErrorUploadSettings
            {
                Enabled = dialog.UploadEnabled,
                Repository = dialog.Repository,
                Branch = string.IsNullOrWhiteSpace(dialog.Branch) ? "main" : dialog.Branch,
                SendScreenshot = dialog.SendScreenshot,
                RecentLogLines = dialog.RecentLogLines,
            };
            _errorUploader.SaveSettings(settings, string.IsNullOrWhiteSpace(dialog.Token) ? null : dialog.Token);
            _log.Write(settings.Enabled
                ? $"[에러전송] 설정 저장 완료 · repo={settings.Repository} · branch={settings.Branch} · screenshot={(settings.SendScreenshot ? "ON" : "OFF")}"
                : "[에러전송] 자동 전송 OFF");
        }
        catch (Exception ex)
        {
            _log.Write("[에러전송] 설정 저장 실패: " + ex.Message);
            MessageBox.Show(this, "에러 전송 설정을 저장하지 못했습니다.\n" + ex.Message, "Mabi Auto", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        await Task.CompletedTask;
    }
}
