#if STAGE16_PREVIEW_ONLY
using System.ComponentModel;
using System.Diagnostics;
using FishingAutomation.Telemetry;

namespace FishingAutomation;

/// <summary>
/// The test build's sole window. Never construct MainForm, FishingBot,
/// Updater, TelegramNotifier, AiAutopilotHost, input driver or packet capture.
/// Running preview never issues a CLI query until a real user clicks.
/// </summary>
internal sealed class Stage16ReadOnlyPreviewForm : Form
{
    private readonly Button _run;
    private readonly Label _state;
    private readonly TextBox _report;
    private CancellationTokenSource? _cts;
    private bool _busy;

    internal Stage16ReadOnlyPreviewForm()
    {
        Text = "MabiAuto 16단계 · 읽기 전용 테스트";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(750, 510);
        MinimumSize = new Size(650, 420);
        Font = new Font("맑은 고딕", 9.3f);
        FormBorderStyle = FormBorderStyle.Sizable;

        var note = new Label
        {
            Dock = DockStyle.Top, Height = 86, Padding = new Padding(16, 10, 12, 6),
            Text = "16단계 분리 진단 · 운영 매크로 기능은 실행되지 않습니다.\r\n" +
                   "게임을 켜고, 기존 모비매크로는 종료한 뒤 [CLI 1회 확인]을 누르세요.\r\n" +
                   "게임 입력·패킷 수집·AI 호출·업데이트·텔레그램·자동 재시도 없음.",
            TextAlign = ContentAlignment.MiddleLeft
        };
        var actions = new Panel { Dock = DockStyle.Top, Height = 56 };
        _run = new Button
        {
            Text = "CLI 1회 확인",
            Width = 168, Height = 39,
            Location = new Point(16, 6)
        };
        _run.Click += async (_, _) => await CheckOnceAsync();
        _state = new Label
        {
            Text = "대기 · CLI 조회 0회",
            Location = new Point(197, 9), AutoSize = true,
            MinimumSize = new Size(350, 32),
            TextAlign = ContentAlignment.MiddleLeft
        };
        actions.Controls.Add(_run);
        actions.Controls.Add(_state);
        _report = new TextBox
        {
            ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            WordWrap = true, Font = new Font("Consolas", 10f),
            Text = "버튼을 누르기 전에는 어떤 CLI도 실행하지 않습니다.\r\n" +
                   "상태 조회의 성공은 서버 상태나 게임 이벤트의 검증을 뜻하지 않습니다.\r\n" +
                   "결과는 이 창에만 표시하고 자동으로 저장하거나 전송하지 않습니다."
        };
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 14) };
        wrap.Controls.Add(_report);
        Controls.Add(wrap);
        Controls.Add(actions);
        Controls.Add(note);

        FormClosing += (_, _) =>
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }
            AiPassiveCliEvidence.SetEnabled(false);
        };
    }

    private static bool AnotherMacroProcessRunning()
    {
        // A separate production FishingAutomation.exe may already be
        // running. This is a conservative *same-exe-name* veto only;
        // it cannot prove absence of any differently named automation.
        try
        {
            using var mine = Process.GetCurrentProcess();
            string name = mine.ProcessName;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id != mine.Id)
                        return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or
            InvalidOperationException or UnauthorizedAccessException)
        {
            return true; // Cannot prove isolation => no CLI query.
        }
    }

    private async Task CheckOnceAsync()
    {
        string gate = Stage16PreviewGate.Check(
            explicitClick: true,
            anotherMacroProcess: AnotherMacroProcessRunning(),
            diagnosticInProgress: _busy);
        if (gate != "allowed")
        {
            _state.Text = "조회 차단";
            _report.Text = gate switch
            {
                "close_other_macro_first" =>
                    "다른 모비매크로 프로세스가 실행 중이거나 상태 확인이 불가능합니다. 기존 매크로를 종료한 뒤 다시 시도하세요.",
                "single_session_only" => "현재 1회 조회가 끝날 때까지 기다리세요.",
                _ => "직접 버튼을 누른 경우에만 조회할 수 있습니다."
            };
            return;
        }

        _busy = true;
        _run.Enabled = false;
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _cts = cts;
        // Never create a directory: this intentionally invalid child
        // directory makes legacy AppLog best-effort writes no-ops. The
        // existing MabinogiMobileCli only needs its logging interface.
        // It does not get an output path that belongs to production.
        string uncreatedLogPath = Path.Combine(Path.GetTempPath(),
            "MabiAuto-Stage16-NoLog-" + Guid.NewGuid().ToString("N"),
            "unwritten.log");
        var cli = new MabinogiMobileCli(new AppLog(uncreatedLogPath, 1024),
            zeroWingMode: true);
        var engine = new CommonObservationEngine(
            diagnosticsEnabled: true,
            allowedSources: new[] { CommonObservationSource.ReadOnlyCli });
        var collector = ReadOnlyCliObservationBridge.Create(cli, engine,
            explicitlyEnabled: true);
        var rows = new List<string> {
            "1회 진단 · 확인된 읽기 전용 CLI 조회만 사용",
            "현재 게임 상태의 확증 / 패킷 의미 해석 / 오류율 개선: 미검증",
            ""
        };
        try
        {
            _state.Text = "조회 중 · 취소하려면 창 닫기";
            AiPassiveCliEvidence.SetEnabled(true); // simulated error-time evidence; no real AI worker
            foreach (CliDiagnosticQuery query in Stage16PreviewGate.ExactReadOnlyQueries)
            {
                cts.Token.ThrowIfCancellationRequested();
                var read = await collector.ReadOnceAsync(query, cts.Token);
                rows.Add(CommonObservationDiagnosticReport.Render(query, read));
                if (!IsDisposed)
                    _report.Text = string.Join(Environment.NewLine, rows);
            }

            // An in-memory **simulated** error snapshot. No error is
            // written or injected into the user AI runtime report folder.
            string? synthetic = AiPassiveCliEvidence.SnapshotForError(
                DateTimeOffset.UtcNow);
            rows.Add("");
            rows.Add(synthetic is null
                ? "AI 증거 모의 확인: 오류 전 5초 내 유효한 CLI 분류 없음"
                : "AI 증거 모의 확인: 오류 전 5초 범위의 분류 형식 생성 가능");
            rows.Add("이것은 실제 AI 오류 처리나 Codex 분석 성공의 검증이 아닙니다.");
            rows.Add("진단 종료: 결과는 저장·업로드되지 않았습니다.");
            if (!IsDisposed)
            {
                _report.Text = string.Join(Environment.NewLine, rows);
                _state.Text = "조회 완료 · 추가 조회 없음";
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                _state.Text = "조회 중단";
                _report.Text = "CLI 조회가 취소되거나 제한시간을 초과했습니다. 게임 입력은 실행하지 않았습니다.";
            }
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                _state.Text = "조회 실패";
                _report.Text = "진단 조회를 완료하지 못했습니다. 원본 오류·CLI 출력은 표시하지 않습니다.";
            }
        }
        finally
        {
            AiPassiveCliEvidence.SetEnabled(false);
            engine.Clear();
            if (ReferenceEquals(_cts, cts))
                _cts = null;
            cts.Dispose();
            _busy = false;
            if (!IsDisposed)
                _run.Enabled = true;
        }
    }
}
#endif
