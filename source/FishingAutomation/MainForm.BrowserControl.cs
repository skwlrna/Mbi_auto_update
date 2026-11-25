namespace FishingAutomation;

public sealed partial class MainForm
{
    private BrowserControlServer? _browserControl;

    private void StopBrowserControl()
    {
        _browserControl?.Dispose();
        _browserControl = null;
    }

    private void ShowBrowserControlSettings()
    {
        using var dialog = new Form
        {
            Text = "브라우저 원격 제어", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(600, 330), MinimumSize = new Size(520, 300),
            Font = new Font("맑은 고딕", 10f), BackColor = Color.FromArgb(245,246,248),
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var help = new Label { Dock = DockStyle.Fill, Text =
            "기본 비활성화 · PC에서 직접 켜야 합니다. 휴대폰 접속은 별도 비공개 HTTPS 프록시가 필요하며 포트 공개는 금지합니다." };
        var address = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = BrowserControlServer.Address };
        var key = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "브라우저 연결 키" };
        var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var start = new Button { Text = "원격 제어 켜기", AutoSize = true };
        var stop = new Button { Text = "끄기 / 키 폐기", AutoSize = true };
        var close = new Button { Text = "닫기", AutoSize = true, DialogResult = DialogResult.OK };
        void RefreshState()
        {
            bool running = _browserControl?.IsRunning == true;
            key.Text = running ? _browserControl!.Token : "";
            start.Enabled = !running;
            stop.Enabled = running;
            status.Text = running ? "활성화 · 키를 외부에 공유하지 마세요." : "비활성화";
        }
        start.Click += (_, _) =>
        {
            try
            {
                StopBrowserControl();
                _browserControl = new BrowserControlServer(DispatchBrowserCommandAsync);
                RefreshState();
            }
            catch (Exception ex)
            {
                StopBrowserControl(); RefreshState();
                _log.Write("[브라우저 제어] 로컬 시작 실패: " + ex.GetType().Name);
                status.Text = "시작 실패 · PC 포트 및 HTTP 권한 확인 필요";
            }
        };
        stop.Click += (_, _) => { StopBrowserControl(); RefreshState(); };
        buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(close);
        layout.Controls.Add(help, 0, 0); layout.Controls.Add(address, 0, 1);
        layout.Controls.Add(key, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "주소와 연결 키는 직접 복사하세요. 끄거나 PC 프로그램을 종료하면 키가 폐기됩니다." }, 0, 5);
        dialog.Controls.Add(layout); dialog.CancelButton = close;
        RefreshState();
        dialog.ShowDialog(this);
    }

    private Task<string> DispatchBrowserCommandAsync(string command, string key, CancellationToken ct)
    {
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || Disposing || ct.IsCancellationRequested)
            return Task.FromResult("PC 연결이 종료되었습니다.");
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing || ct.IsCancellationRequested)
                { reply.TrySetResult("PC 연결이 종료되었습니다."); return; }
                try
                {
                    if (command == "/status") reply.TrySetResult(GetRemoteStatus());
                    else if (command == "/stop")
                    { StopSelected(); reply.TrySetResult("전체 정지 요청 접수"); }
                    else if (command == "가공 목록")
                        reply.TrySetResult(HandlePhoneAlteringCommand(command, "browser:read"));
                    else if (command.StartsWith("가공 ", StringComparison.Ordinal) && key.StartsWith("browser:", StringComparison.Ordinal))
                        reply.TrySetResult(HandlePhoneAlteringCommand(command, key));
                    else if (command.StartsWith("어비스 시작 ", StringComparison.Ordinal) && key.StartsWith("browser:", StringComparison.Ordinal))
                        reply.TrySetResult(StartBrowserAbyss(command["어비스 시작 ".Length..]));
                    else reply.TrySetResult("지원하지 않는 명령입니다.");
                }
                catch (Exception ex)
                {
                    _log.Write("[브라우저 제어] 명령 실패: " + ex.GetType().Name);
                    reply.TrySetResult("명령을 처리하지 못했습니다. PC 상태를 확인하세요.");
                }
            }));
        }
        catch (InvalidOperationException) { reply.TrySetResult("PC 연결이 종료되었습니다."); }
        return reply.Task.WaitAsync(ct);
    }

    private string StartBrowserAbyss(string dungeon)
    {
        if (AnyRunning || _activeMode is not null || _gatheringDiagnosticRunning)
            return "다른 작업 진행 중 · 어비스 시작 차단";
        if (dungeon is not ("허상의 정박지" or "광기의 동굴" or "흩어진 물길"))
            return "지원하지 않는 던전입니다.";
        int index = _abyssDungeon.Items.IndexOf(dungeon);
        if (index < 0 || _referenceDashboard is null) return "던전 설정을 확인하세요.";
        _referenceDashboard.NavigateToMode(2);
        if (SelectedMode != "어비스") return "어비스 모드 선택 실패";
        _abyssDungeon.SelectedIndex = index;
        if (SelectedAbyssDungeon != dungeon) return "던전 선택 실패";
        StartSelected();
        return _statusValue.Text;
    }
}
