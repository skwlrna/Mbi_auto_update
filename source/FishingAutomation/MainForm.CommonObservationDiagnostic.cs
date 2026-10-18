using FishingAutomation.Telemetry;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private Form? _commonObservationWindow;
    private CancellationTokenSource? _commonObservationReadCts;
    private bool _commonObservationReadBusy;

    /// <summary>
    /// Only the visible user click opens this optional diagnostics window.
    /// Merely opening it never reads the CLI.
    /// </summary>
    private void ShowCommonObservationDiagnostic()
    {
        if (_commonObservationWindow is { IsDisposed: false })
        {
            _commonObservationWindow.BringToFront();
            _commonObservationWindow.Activate();
            return;
        }

        var window = new Form
        {
            Text = "게임 상태 관측 · 읽기 전용",
            ClientSize = new Size(750, 438),
            MinimumSize = new Size(580, 340),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            Font = new Font("맑은 고딕", 9f),
            BackColor = PanelBg
        };
        var intro = new Label
        {
            Text = "사용자가 [CLI 1회 조회]를 눌렀을 때만 조회합니다. 자동화 실행 중에는 조회하지 않습니다.",
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(15, 8, 12, 0),
            ForeColor = TitleText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var commandRow = new Panel
        {
            Dock = DockStyle.Top, Height = 52, Padding = new Padding(14, 4, 14, 4)
        };
        var button = new Button
        {
            Text = "CLI 1회 조회",
            Dock = DockStyle.Right,
            Width = 140,
            FlatStyle = FlatStyle.Flat,
            BackColor = Blue,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        var status = new Label
        {
            Text = "대기 · 자동 조회 없음",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TitleText
        };
        commandRow.Controls.Add(button);
        commandRow.Controls.Add(status);
        var output = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 9f),
            Text = "연결, 활동, 가공 작업, 인벤토리, 레시피를 읽기 전용으로 확인합니다.\r\n" +
                "게임 상태 판정은 기존 CLI에 한정되며 패킷 디코딩은 하지 않습니다.\r\n" +
                "개인정보·아이템명·CLI 원문은 표시하거나 저장하지 않습니다."
        };
        var foot = new Label
        {
            Text = "진단 결과는 게임 입력, 가공 완료, 수령·재시도·AI 수정 실행을 허가하지 않습니다.",
            Dock = DockStyle.Bottom,
            Height = 39,
            ForeColor = Color.FromArgb(100, 116, 139),
            Padding = new Padding(14, 4, 14, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 4) };
        body.Controls.Add(output);
        window.Controls.Add(body);
        window.Controls.Add(foot);
        window.Controls.Add(commandRow);
        window.Controls.Add(intro);

        button.Click += async (_, _) =>
            await RunCommonObservationDiagnosticOnceAsync(window, button, status, output);
        window.FormClosed += (_, _) =>
        {
            try { _commonObservationReadCts?.Cancel(); }
            catch (ObjectDisposedException) { }
            if (ReferenceEquals(_commonObservationWindow, window))
                _commonObservationWindow = null;
        };
        _commonObservationWindow = window;
        window.Show(this); // Modeless; F10 and the main controls stay accessible.
    }

    private async Task RunCommonObservationDiagnosticOnceAsync(
        Form window, Button button, Label status, TextBox output)
    {
        string gate = CommonObservationManualUiPolicy.Evaluate(
            userClicked: true, anyAutomationRunning: AnyRunning,
            starting: _starting, productionPolling: _productionPolling,
            anotherDiagnostic: _commonObservationReadBusy || _gatheringDiagnosticRunning);
        if (gate != "allowed")
        {
            if (!window.IsDisposed)
            {
                status.Text = "조회 보류";
                output.Text = gate == "automation_busy"
                    ? "현재 매크로 또는 생산 조회가 실행 중입니다. 작업 종료 후 다시 조회하세요."
                    : "이미 진단이 실행 중입니다. 완료 후 다시 조회하세요.";
            }
            return;
        }

        _commonObservationReadBusy = true;
        _starting = true; // Existing F9 guards reject starts while diagnostic queries execute.
        button.Enabled = false;
        using var cts = new CancellationTokenSource();
        _commonObservationReadCts = cts;

        var engine = new CommonObservationEngine(
            diagnosticsEnabled: true,
            allowedSources: new[] {
                CommonObservationSource.ReadOnlyCli,
                CommonObservationSource.LocalScheduler
            });
        var collector = ReadOnlyCliObservationBridge.Create(
            _cli, engine, explicitlyEnabled: true);
        var lines = new List<string>
        {
            "사용자 요청에 따른 1회 읽기 전용 진단",
            "실시간 모비레이 이벤트 API 연결: 미검증",
            "자동화 동작 변경: 없음",
            ""
        };
        try
        {
            status.Text = "조회 중 · CLI 응답 대기";
            output.Text = string.Join(Environment.NewLine, lines);
            var queries = new[]
            {
                CliDiagnosticQuery.Status,
                CliDiagnosticQuery.Activity,
                CliDiagnosticQuery.AlteringWorks,
                CliDiagnosticQuery.Items,
                CliDiagnosticQuery.AlteringCatalog
            };
            foreach (var query in queries)
            {
                cts.Token.ThrowIfCancellationRequested();
                var read = await collector.ReadOnceAsync(query, cts.Token);
                lines.Add(CommonObservationDiagnosticReport.Render(query, read));
                if (query == CliDiagnosticQuery.AlteringWorks)
                {
                    // Without a current, separately verified local production
                    // stage there can be NO meaningful live mismatch verdict.
                    // The running macro is deliberately excluded from inspection.
                    var audit = collector.CompareMultiAltering(null, read);
                    lines.Add("가공 단계 비교: " +
                        CommonObservationDiagnosticReport.ComparisonName(audit.Status));
                }
                if (!window.IsDisposed && !IsDisposed)
                    output.Text = string.Join(Environment.NewLine, lines);
            }
            if (!window.IsDisposed)
            {
                lines.Add("");
                lines.Add("단일 조회 결과는 자동 완료·재료 부족·수령 증거가 아닙니다.");
                output.Text = string.Join(Environment.NewLine, lines);
                status.Text = "조회 완료 · 자동 반복 없음";
            }
        }
        catch (OperationCanceledException)
        {
            if (!window.IsDisposed)
            {
                status.Text = "조회 취소";
                output.Text = "읽기 전용 상태 조회가 취소되었습니다. 게임 동작은 변경하지 않았습니다.";
            }
        }
        catch (Exception)
        {
            // The modeless click handler must never throw an unobserved UI
            // exception, and it must never reveal raw CLI exception messages.
            if (!window.IsDisposed)
            {
                status.Text = "조회 실패";
                output.Text = "일부 조회를 완료하지 못했습니다. CLI 오류 상세 내용은 표시하지 않습니다.";
            }
        }
        finally
        {
            if (ReferenceEquals(_commonObservationReadCts, cts))
                _commonObservationReadCts = null;
            _starting = false;
            _commonObservationReadBusy = false;
            engine.Clear();
            if (!window.IsDisposed && !IsDisposed)
                button.Enabled = true;
        }
    }
}
