namespace FishingAutomation;

public sealed partial class MainForm
{
    private ReleaseUpdateInfo? _pendingUpdate;
    private bool _updateCheckRunning;

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updateCheckRunning || IsDisposed) return;
        _updateCheckRunning = true;
        try
        {
            Ui(() =>
            {
                if (_updateButton is not null) _updateButton.Enabled = false;
                if (_updateStatusValue is not null) _updateStatusValue.Text = "확인 중...";
            });

            var info = await UpdateManager.CheckLatestAsync();
            _pendingUpdate = info;
            if (info is null)
            {
                Ui(() =>
                {
                    _updateStatusValue.Text = $"최신 {UpdateManager.CurrentVersion}";
                    _updateStatusValue.ForeColor = Green;
                    _updateButton.Text = "업데이트 확인";
                });
                if (userInitiated)
                    MessageBox.Show($"현재 {UpdateManager.CurrentVersion}이 최신 버전입니다.", "MABI AUTO 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Ui(() =>
            {
                _updateStatusValue.Text = $"새 버전 {info.Tag}";
                _updateStatusValue.ForeColor = Color.FromArgb(255, 194, 87);
                _updateButton.Text = $"{info.Tag} 업데이트";
                _updateButton.Enabled = true;
            });

            // 시작 시 발견한 새 버전은 자동으로 내려받아 적용한다.
            // 사용자가 버튼을 눌러 확인한 경우에는 기존처럼 확인창을 보여준다.
            await InstallPendingUpdateAsync(askConfirmation: userInitiated);
        }
        catch (Exception ex)
        {
            Ui(() =>
            {
                _updateStatusValue.Text = "확인 실패";
                _updateStatusValue.ForeColor = Color.Salmon;
            });
            if (userInitiated)
                MessageBox.Show("업데이트 확인에 실패했습니다.\r\n\r\n" + ex.Message, "MABI AUTO 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
            Ui(() => { if (_updateButton is not null) _updateButton.Enabled = true; });
        }
    }

    private async Task InstallPendingUpdateAsync(bool askConfirmation = true)
    {
        var info = _pendingUpdate;
        if (info is null)
        {
            await CheckForUpdatesAsync(true);
            return;
        }
        if (AnyRunning)
        {
            MessageBox.Show("매크로 실행 중에는 업데이트할 수 없습니다. F10으로 정지한 뒤 다시 시도하세요.", "MABI AUTO 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string notes = string.IsNullOrWhiteSpace(info.Notes) ? "새 버전이 준비되었습니다." : info.Notes;
        if (notes.Length > 700) notes = notes[..700] + "...";
        if (askConfirmation)
        {
            var result = MessageBox.Show(
                $"{UpdateManager.CurrentVersion} → {info.Tag} 업데이트가 있습니다.\r\n\r\n{notes}\r\n\r\n설정, 텔레그램 정보, 이미지 템플릿은 유지합니다. SHA256 검증 후 설치하며, 새 버전 실행 실패 시 이전 버전으로 자동 복구합니다. 지금 업데이트할까요?",
                "MABI AUTO 업데이트",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
        }
        else
        {
            _log.Write($"[업데이트] 자동 업데이트 시작: {UpdateManager.CurrentVersion} -> {info.Tag}");
        }

        try
        {
            _updateButton.Enabled = false;
            _updateStatusValue.Text = "다운로드 0%";
            var progress = new Progress<int>(p =>
            {
                if (!IsDisposed) _updateStatusValue.Text = $"다운로드 {p}%";
            });
            string zip = await UpdateManager.DownloadAsync(info, progress);
            _updateStatusValue.Text = "검증 완료 · 업데이트 준비";
            _log.Write($"[업데이트] {info.Tag} 다운로드 완료: {info.AssetName}");
            UpdateManager.LaunchUpdater(zip);
            _watchdog.Dispose();
            Application.Exit();
        }
        catch (Exception ex)
        {
            _updateButton.Enabled = true;
            _updateStatusValue.Text = "업데이트 실패";
            _updateStatusValue.ForeColor = Color.Salmon;
            MessageBox.Show("업데이트 적용에 실패했습니다.\r\n\r\n" + ex.Message, "MABI AUTO 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
