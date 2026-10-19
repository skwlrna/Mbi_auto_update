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
    private readonly HashSet<string> _sentAiAlertKeys = new(StringComparer.Ordinal);
    private static string AiPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "ai-autopilot.json");

    private Control BuildAiAutopilotPanel()
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = PanelBg,
            Padding = new Padding(12, 6, 12, 6) };
        var title = new Label { Text = "AI 오류 관리", Dock = DockStyle.Left, Width = 110,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = TitleText,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold) };
        _aiToggle = new Button { Text = "AI 분석 시작", Dock = DockStyle.Right, Width = 111,
            BackColor = CardBg2, ForeColor = TitleText, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("맑은 고딕", 8.5f), Cursor = Cursors.Hand };
        _aiToggle.FlatAppearance.BorderColor = Border;
        _aiToggle.FlatAppearance.MouseOverBackColor = AccentSoft;
        _aiToggle.Click += (_, _) => ToggleAiAutopilot();
        _aiStartWithApp = new CheckBox { Text = "다음 실행부터 자동 시작", Dock = DockStyle.Right,
            Width = 184, ForeColor = TitleText, BackColor = row.BackColor,
            Font = new Font("맑은 고딕", 8f), TextAlign = ContentAlignment.MiddleLeft };
        _aiStartWithApp.CheckedChanged += (_, _) => { if (_aiInitialized) SaveAiPreferences(); };
        _aiAutoDraftPr = new CheckBox
        {
            Text = "테스트 통과 시 GitHub에 수정안 저장", Dock = DockStyle.Right, Width = 225,
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
            Text = "오류 시간별 내역", Dock = DockStyle.Right, Width = 120,
            BackColor = CardBg2, ForeColor = TitleText,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 8.5f)
        };
        dashboard.FlatAppearance.BorderColor = Border;
        dashboard.FlatAppearance.MouseOverBackColor = AccentSoft;
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
        _aiState = new Label { Text = "AI 분석이 켜지면 오류 일부를 Codex가 검토합니다", Dock = DockStyle.Fill,
            AutoEllipsis = true, ForeColor = Muted,
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
        _aiHost.NotificationRequested += (category, eventId) =>
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => _ = SendAiAutopilotAlertAsync(category, eventId)));
            }
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

    // Read one bounded local result, and map only known machine-readable reasons.
    // Never send raw logs, paths, screenshots, free-form Codex output or prompts.
    private static string AiAlertReason(string eventId)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MabiAutoAI", "autopilot-results", eventId, "result.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 2 || info.Length > 131072) return "상세 내용은 AI 오류 시간별 내역에서 확인";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("event_id", out var id) || id.GetString() != eventId ||
                !root.TryGetProperty("reason", out var value) ||
                value.ValueKind != JsonValueKind.String)
                return "상세 내용은 AI 오류 시간별 내역에서 확인";
            string reason = value.GetString() ?? "";
            if (reason.Contains("codex_quota_exhausted", StringComparison.Ordinal))
                return "Codex 사용량 한도로 중단";
            if (reason.Contains("build_or_regression_test_failed", StringComparison.Ordinal))
                return "빌드 또는 회귀 테스트 실패";
            if (reason.Contains("agent_no_patch", StringComparison.Ordinal))
                return "Codex가 수정 코드를 생성하지 못함";
            if (reason.Contains("repo_dirty", StringComparison.Ordinal))
                return "작업용 소스가 변경되어 안전 중단";
            if (reason.Contains("codex_unverified", StringComparison.Ordinal))
                return "Codex 실행 또는 검증 실패";
            if (reason.Contains("release_source", StringComparison.Ordinal))
                return "배포 기준 소스 검증 실패";
            if (reason.Contains("regression", StringComparison.Ordinal))
                return "필수 회귀 테스트 조건 미충족";
            return "상세 내용은 AI 오류 시간별 내역에서 확인";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or InvalidOperationException)
        {
            return "상세 내용은 AI 오류 시간별 내역에서 확인";
        }
    }

    private async Task SendAiAutopilotAlertAsync(string category, string eventId)
    {
        var settings = _notifier.Settings;
        if (!settings.Enabled || !settings.AiAutopilotAlertsEnabled ||
            string.IsNullOrWhiteSpace(settings.BotToken) ||
            string.IsNullOrWhiteSpace(settings.ChatId)) return;

        string key = category + ":" + eventId;
        if (!_sentAiAlertKeys.Add(key)) return;
        // Keep an upper bound: the watcher itself persists event deduplication.
        if (_sentAiAlertKeys.Count > 512) _sentAiAlertKeys.Clear();

        string title = category switch
        {
            "started" => "AI 오류 분석 시작",
            "tests_passed_review_required" => "AI 코드 테스트 통과 (미적용)",
            "needs_review" => "AI 수정안 검토 필요",
            "tests_failed" => "AI 수정 테스트 실패",
            "blocked" => "AI 분석·수정 중단",
            _ => ""
        };
        if (title.Length == 0) return;
        string detail = category switch
        {
            "started" => "격리된 작업 폴더에서 오류 분석을 시작했습니다.",
            "tests_passed_review_required" =>
                "코드 테스트 결과만 통과했습니다. 실제 게임 해결·운영 적용은 확인되지 않았습니다.",
            _ => AiAlertReason(eventId)
        };
        await _notifier.SendAlertAsync(title, "오류 ID: " + eventId +
            "\n" + detail + "\n진행 내역: MabiAuto → AI 오류 시간별 내역",
            screenshot: false);
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
        if (_aiState is not null) _aiState.Text = text + " · 로그 일부 Codex 전송 가능";
        if (_aiToggle is not null) _aiToggle.Text = _aiHost?.IsRunning == true ? "AI 분석 중지" : "AI 분석 시작";
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