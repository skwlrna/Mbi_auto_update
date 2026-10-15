using System.Text.Json;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private AiAutopilotHost? _aiHost;
    private Label? _aiState;
    private Button? _aiToggle;
    private CheckBox? _aiStartWithApp;
    private CheckBox? _aiAutoDraftPr;
    private AiAutopilotDashboard? _aiDashboard;
    private bool _aiInitialized;
    private string? _lastAiLoggedStatus;
    private static string AiPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "ai-autopilot.json");

    private Control BuildAiAutopilotPanel()
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(245, 248, 253),
            Padding = new Padding(12, 6, 12, 6) };
        var title = new Label { Text = "AI 자동수정", Dock = DockStyle.Left, Width = 110,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = TitleText,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold) };
        _aiToggle = new Button { Text = "AI 감시 시작", Dock = DockStyle.Right, Width = 105,
            BackColor = Color.White, ForeColor = TitleText, FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.5f), Cursor = Cursors.Hand };
        _aiToggle.FlatAppearance.BorderColor = Border;
        _aiToggle.Click += (_, _) => ToggleAiAutopilot();
        _aiStartWithApp = new CheckBox { Text = "다음 실행부터 자동 시작", Dock = DockStyle.Right,
            Width = 184, ForeColor = TitleText, BackColor = row.BackColor,
            Font = new Font("맑은 고딕", 8f), TextAlign = ContentAlignment.MiddleLeft };
        _aiStartWithApp.CheckedChanged += (_, _) => { if (_aiInitialized) SaveAiPreferences(); };
        _aiAutoDraftPr = new CheckBox
        {
            Text = "테스트 통과 후 수정안 등록", Dock = DockStyle.Right, Width = 188,
            ForeColor = TitleText, BackColor = row.BackColor,
            Font = new Font("맑은 고딕", 8f), TextAlign = ContentAlignment.MiddleLeft
        };
        _aiAutoDraftPr.CheckedChanged += (_, _) =>
        {
            if (!_aiInitialized) return;
            if (_aiAutoDraftPr.Checked &&
                MessageBox.Show(this,
                    "AI 수정이 모든 테스트를 통과하면 GitHub에 수정 브랜치를 올리고 Draft PR을 자동 생성합니다.\n" +
                    "자동 병합·배포는 하지 않습니다. GitHub CLI 인증이 필요합니다.\n" +
                    "이 기능을 켜겠습니까?",
                    "GitHub PR 자동 생성 동의", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _aiAutoDraftPr.Checked = false;
                return;
            }
            SaveAiPreferences();
        };
        var dashboard = new Button
        {
            Text = "오류 기록 보기", Dock = DockStyle.Right, Width = 110,
            BackColor = Color.White, ForeColor = TitleText,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 8.5f)
        };
        dashboard.FlatAppearance.BorderColor = Border;
        dashboard.Click += (_, _) =>
        {
            if (_aiDashboard is { IsDisposed: false })
            {
                _aiDashboard.BringToFront();
                _aiDashboard.Activate();
                return;
            }
            _aiDashboard = new AiAutopilotDashboard(
                () => _aiHost?.IsRunning == true, () => _aiHost?.Status ?? "대기 중");
            _aiDashboard.FormClosed += (_, _) => _aiDashboard = null;
            _aiDashboard.Show(this); // modeless: F9/F10 and primary controls remain accessible
        };
        _aiState = new Label { Text = "AI 감시가 꺼져 있습니다 · 시작하면 새 오류부터 분석합니다", Dock = DockStyle.Fill,
            AutoEllipsis = true, ForeColor = Color.FromArgb(82, 99, 120),
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f) };
        row.Controls.Add(_aiState);
        row.Controls.Add(_aiStartWithApp);
        row.Controls.Add(_aiAutoDraftPr);
        row.Controls.Add(dashboard);
        row.Controls.Add(_aiToggle);
        row.Controls.Add(title);
        return row;
    }

    private sealed class AiPreferences
    {
        public bool StartWithApp { get; set; }
        public bool AutoCreatePr { get; set; }
    }

    private void InitializeAiAutopilot()
    {
        _aiHost = new AiAutopilotHost();
        _aiHost.StatusChanged += text =>
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => RefreshAiAutopilotControls(text))); }
            catch (InvalidOperationException) { }
        };
        try
        {
            if (File.Exists(AiPreferencesPath))
            {
                var pref = JsonSerializer.Deserialize<AiPreferences>(File.ReadAllText(AiPreferencesPath));
                _aiStartWithApp!.Checked = pref?.StartWithApp ?? false;
                _aiAutoDraftPr!.Checked = pref?.AutoCreatePr ?? false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Write("[AI 관리] 자동 시작 설정을 읽지 못했습니다. 자동 시작은 비활성화됩니다.");
            _aiStartWithApp!.Checked = false;
            _aiAutoDraftPr!.Checked = false;
        }
        _aiInitialized = true;
        RefreshAiAutopilotControls(_aiHost.Status);
    }

    private void SaveAiPreferences()
    {
        try
        {
            string path = AiPreferencesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new AiPreferences
            {
                StartWithApp = _aiStartWithApp?.Checked ?? false,
                AutoCreatePr = _aiAutoDraftPr?.Checked ?? false
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("[AI 관리] 자동 시작 설정 저장 실패: " + ex.GetType().Name);
        }
    }

    private void ToggleAiAutopilot()
    {
        if (_aiHost is null) return;
        if (_aiHost.IsRunning) _aiHost.Stop();
        else _aiHost.Start();
        RefreshAiAutopilotControls(_aiHost.Status);
    }

    private void RefreshAiAutopilotControls(string text)
    {
        if (_aiState is not null) _aiState.Text = text;
        if (_aiToggle is not null) _aiToggle.Text = _aiHost?.IsRunning == true ? "감시 중지" : "AI 감시 시작";
        // State already passed through AiAutopilotHost's safe/status-only filter.
        // Persist every real state transition to the ordinary runtime log;
        // duplicate UI refreshes must not generate duplicate log lines.
        if (!string.Equals(_lastAiLoggedStatus, text, StringComparison.Ordinal))
        {
            _lastAiLoggedStatus = text;
            _log.Write("[AI 감시] " + text);
        }
    }

    private void AutoStartAiAutopilot()
    {
        if (_aiStartWithApp?.Checked == true && _aiHost is { IsRunning: false })
            _aiHost.Start();
    }

    private void DisposeAiAutopilot()
    {
        if (_aiHost?.IsRunning == true)
            _log.Write("[AI 감시] 프로그램 종료 · AI 감시와 진행 중인 작업 함께 중지");
        _aiHost?.Dispose();
    }
}