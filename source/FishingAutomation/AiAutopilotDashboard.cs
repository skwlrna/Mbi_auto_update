using System.Diagnostics;
using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// Read-only local snapshot of the existing Autopilot result directory.
/// No process control, script invocation, upload or automatic PR operations.
/// </summary>
internal sealed class AiAutopilotDashboard : Form
{
    private readonly Func<bool> _isWatcherRunning;
    private readonly Func<string> _watcherStatus;
    private readonly Label _overview;
    private readonly Label _counts;
    private readonly Label _updated;
    private readonly Label _memory;
    private readonly DataGridView _recent;
    private readonly System.Windows.Forms.Timer _timer;

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "MabiAutoAI", "autopilot-results");

    internal AiAutopilotDashboard(Func<bool> isWatcherRunning, Func<string> watcherStatus)
    {
        _isWatcherRunning = isWatcherRunning;
        _watcherStatus = watcherStatus;
        Text = "MabiAuto · AI 자동화 현황";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 460);
        ClientSize = new Size(940, 575);
        BackColor = Color.FromArgb(246, 248, 252);
        Font = new Font("맑은 고딕", 9f);

        var heading = new Label
        {
            Text = "AI 자동화 대시보드", Dock = DockStyle.Top, Height = 49,
            Font = new Font("맑은 고딕", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(23, 32, 51),
            Padding = new Padding(17, 10, 0, 0)
        };
        _overview = new Label
        {
            Dock = DockStyle.Top, Height = 60, AutoEllipsis = true,
            ForeColor = Color.FromArgb(51, 68, 91),
            Padding = new Padding(18, 6, 12, 0)
        };
        _counts = new Label
        {
            Dock = DockStyle.Top, Height = 59,
            ForeColor = Color.FromArgb(23, 32, 51),
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Padding = new Padding(18, 5, 12, 0)
        };
        _memory = new Label
        {
            Text = "오류 학습 기록: 아직 없음 · 과거 AI 분석이 해결을 증명하지는 않음",
            Dock = DockStyle.Top, Height = 34,
            ForeColor = Color.FromArgb(70, 89, 112),
            Padding = new Padding(18, 5, 12, 0)
        };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 51 };
        _updated = new Label
        {
            Text = "로컬 기록만 표시 · 자동 병합/배포 없음",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(82, 99, 120),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0)
        };
        var refresh = new Button
        {
            Text = "새로고침", Dock = DockStyle.Right, Width = 105,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        refresh.Click += (_, _) => Reload();
        foot.Controls.Add(_updated);
        foot.Controls.Add(refresh);

        _recent = new DataGridView
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White, GridColor = Color.FromArgb(224, 232, 242)
        };
        _recent.ColumnHeadersHeight = 34;
        _recent.RowTemplate.Height = 29;
        _recent.Columns.Add("event", "오류 ID");
        _recent.Columns.Add("status", "AI 분석 결과");
        _recent.Columns.Add("pr", "검토용 GitHub PR");
        _recent.Columns[0].FillWeight = 165;
        _recent.Columns[1].FillWeight = 100;
        _recent.Columns[2].FillWeight = 140;
        _recent.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _recent.Rows.Count) return;
            string url = _recent.Rows[e.RowIndex].Tag as string ?? "";
            if (!IsTrustedPrUrl(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { /* External browser is optional. */ }
        };

        var intro = new Label
        {
            Text = "최근 AI 분석 결과 · PR 행을 더블클릭하면 GitHub에서 열립니다",
            Dock = DockStyle.Top, Height = 28, Padding = new Padding(18, 0, 0, 0),
            ForeColor = Color.FromArgb(82, 99, 120)
        };
        Controls.Add(_recent);
        Controls.Add(intro);
        Controls.Add(foot);
        Controls.Add(_memory);
        Controls.Add(_counts);
        Controls.Add(_overview);
        Controls.Add(heading);

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += (_, _) => Reload();
        Shown += (_, _) => { Reload(); _timer.Start(); };
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); };
    }

    internal static bool IsTrustedPrUrl(string value)
    {
        const string prefix = "https://github.com/skwlrna/Mbi_auto_update/pull/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var id = value[prefix.Length..];
        return id.Length is > 0 and <= 12 && id.All(char.IsDigit) && id[0] != '0';
    }

    private static string Field(JsonElement element, string name, int max = 120)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        return text.Length > max ? text[..max] : text;
    }

    private static int Number(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? Math.Clamp(n, 0, 100000) : 0;
    }

    private static JsonDocument? ReadBounded(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 131072 || file.Length < 2) return null;
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private static string StatusText(string status) => status switch
    {
        "tests_passed_review_required" => "테스트 통과 · 검토 필요",
        "in_progress" => "분석 중 / 미완료",
        "needs_review" => "수정안 검토 필요",
        "tests_failed" => "테스트 실패",
        "blocked" => "진행 차단",
        "duplicate_skipped" => "중복 증거 보존",
        _ => "상태 확인 필요"
    };

    private void Reload()
    {
        if (IsDisposed) return;
        bool running = _isWatcherRunning();
        string active = "", activity = "기록 없음";
        int pending = 0;
        using (var state = ReadBounded(Path.Combine(Root, "watch-activity.json")))
        {
            if (state is not null)
            {
                activity = Field(state.RootElement, "status", 32);
                active = Field(state.RootElement, "active", 100);
                pending = Number(state.RootElement, "pending");
            }
        }
        _overview.Text = (running ? "AI 감시 실행 중" : "AI 감시 꺼짐") +
            " · " + _watcherStatus() +
            "\n마지막 작업: " + (active.Length > 0 ? active : "없음") +
            " · 대기 " + pending + "건" +
            (running ? " · " + activity : " · 이전 상태 기록은 현재 실행 상태가 아님");

        try
        {
            using (var memory = ReadBounded(Path.Combine(Root, "error-memory.json")))
            {
                int stored = 0, candidates = 0;
                if (memory is not null &&
                    memory.RootElement.TryGetProperty("entries", out var entries) &&
                    entries.ValueKind == JsonValueKind.Array)
                {
                    stored = entries.GetArrayLength();
                    foreach (var item in entries.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("verified_offline_tests", out var passed) &&
                            passed.ValueKind == JsonValueKind.True) candidates++;
                    }
                }
                _memory.Text = $"AI 오류 이력 {stored}건 (최대 80건) · " +
                    $"자동 테스트 통과 후보 {candidates}건 · 실게임 해결 확인은 별도";
            }
            var files = Directory.Exists(Root)
                ? Directory.EnumerateDirectories(Root)
                    .Select(x => Path.Combine(x, "result.json"))
                    .Where(File.Exists)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(250).ToArray()
                : Array.Empty<FileInfo>();
            int success = 0, failed = 0, review = 0, activeCount = 0;
            int createdPr = 0;
            _recent.Rows.Clear();
            foreach (var file in files)
            {
                using var report = ReadBounded(file.FullName);
                if (report is null) continue;
                var root = report.RootElement;
                string status = Field(root, "status", 64);
                if (status == "tests_passed_review_required") success++;
                else if (status is "tests_failed" or "blocked") failed++;
                else if (status == "needs_review") review++;
                else if (status == "in_progress") activeCount++;
                string url = "";
                string prText = "없음";
                if (root.TryGetProperty("pr", out var pr) && pr.ValueKind == JsonValueKind.Object)
                {
                    url = Field(pr, "url", 150);
                    string stateName = Field(pr, "status", 40);
                    if (stateName == "draft_created" && IsTrustedPrUrl(url))
                    {
                        createdPr++;
                        prText = "Draft PR " + url[(url.LastIndexOf('/') + 1)..];
                    }
                    else if (stateName == "blocked") prText = "PR 생성 보류";
                    else if (stateName == "disabled") prText = "PR 생성 꺼짐";
                }
                string eventId = Field(root, "event_id", 100);
                if (eventId.Length == 0) eventId = file.Directory?.Name ?? "기록";
                int i = _recent.Rows.Add(eventId, StatusText(status), prText);
                _recent.Rows[i].Tag = IsTrustedPrUrl(url) ? url : "";
                if (_recent.Rows.Count >= 35) break;
            }
            _counts.Text = $"자동 테스트 통과 {success}건     실패/차단 {failed}건     검토 필요 {review}건" +
                $"     미완료 기록 {activeCount}건     생성된 Draft PR {createdPr}건";
            _updated.Text = "마지막 갱신 " + DateTime.Now.ToString("HH:mm:ss") +
                " · 로컬 기록 기준 · PR은 승인 전 자동 병합되지 않음";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _updated.Text = "AI 분석 결과 폴더를 읽지 못했습니다.";
        }
    }
}
