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
    private sealed record ResultItem(string Url, string FilePath, string EventId);
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
        var confirmed = new Button
        {
            Text = "실게임 해결 확인", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        confirmed.Click += (_, _) => SubmitGameFeedback("resolved");
        var recurred = new Button
        {
            Text = "실게임 재발 확인", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        recurred.Click += (_, _) => SubmitGameFeedback("recurred");
        var reset = new Button
        {
            Text = "검증 취소", Dock = DockStyle.Right, Width = 93,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        reset.Click += (_, _) => SubmitGameFeedback("unverified");
        var handoff = new Button
        {
            Text = "ChatGPT 인계 복사", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        handoff.Click += (_, _) => CopyChatGptHandoff();
        var resumeCodex = new Button
        {
            Text = "Codex 한도 재개", Dock = DockStyle.Right, Width = 125,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        resumeCodex.Click += (_, _) => RequestQuotaResume();
        var refresh = new Button
        {
            Text = "새로고침", Dock = DockStyle.Right, Width = 105,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        refresh.Click += (_, _) => Reload();
        foot.Controls.Add(_updated);
        foot.Controls.Add(refresh);
        foot.Controls.Add(handoff);
        foot.Controls.Add(resumeCodex);
        foot.Controls.Add(reset);
        foot.Controls.Add(recurred);
        foot.Controls.Add(confirmed);

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
        _recent.Columns.Add("feedback", "실게임 확인(수동)");
        _recent.Columns.Add("pr", "검토용 GitHub PR");
        _recent.Columns[0].FillWeight = 150;
        _recent.Columns[1].FillWeight = 100;
        _recent.Columns[2].FillWeight = 96;
        _recent.Columns[3].FillWeight = 135;
        _recent.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _recent.Rows.Count) return;
            string url = (_recent.Rows[e.RowIndex].Tag as ResultItem)?.Url ?? "";
            if (!IsTrustedPrUrl(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { /* External browser is optional. */ }
        };

        var intro = new Label
        {
            Text = "수정안을 실제로 설치하고 게임에서 재현 검사한 뒤에만 수동 결과를 기록하세요 · PR 더블클릭 가능",
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

    private static bool ValidEventId(string id)
    {
        if (id.Length is not (16 or 35)) return false;
        string prefix = id.Length == 16 ? id : id[..16];
        return prefix.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            (id.Length == 16 || id == prefix + "-retry-cli-arg-v121");
    }

    private static string ManualVerdict(string resultDir, string eventId)
    {
        try
        {
            string file = Path.Combine(resultDir, "user-feedback.json");
            using var data = ReadBounded(file);
            if (data is null) return "unverified";
            var obj = data.RootElement;
            if (Field(obj, "event_id") != eventId || Field(obj, "source") != "manual_ui")
                return "unverified";
            string value = Field(obj, "verdict");
            return value is "resolved" or "recurred" ? value : "unverified";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "unverified"; }
    }

    private static string VerdictText(string verdict) => verdict switch
    {
        "resolved" => "사용자 해결 확인",
        "recurred" => "사용자 재발 확인",
        _ => "실게임 미확인"
    };

    private void SubmitGameFeedback(string verdict)
    {
        if (_recent.CurrentRow?.Tag is not ResultItem selected ||
            !ValidEventId(selected.EventId))
            return;
        try
        {
            string directory = Path.Combine(Root, selected.EventId);
            string source = Path.Combine(directory, "result.json");
            if (!string.Equals(selected.FilePath, source, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return;
            using var result = ReadBounded(source);
            if (result is null || Field(result.RootElement, "event_id") != selected.EventId ||
                Field(result.RootElement, "status") != "tests_passed_review_required")
            {
                MessageBox.Show(this, "실게임 피드백은 자동 테스트 통과 후 검토 중인 수정안에만 기록할 수 있습니다.");
                return;
            }
            string action = verdict == "resolved" ? "실제 수정 버전을 적용하고 같은 오류가 해결된 것을 직접 확인했습니까?"
                : verdict == "recurred" ? "실제 수정 버전을 적용해 테스트했지만 같은 오류가 다시 발생한 것을 확인했습니까?"
                : "기존 실게임 결과를 미확인 상태로 되돌리겠습니까?";
            if (MessageBox.Show(this, action + "\n이 표시는 사용자 수동 피드백이며 자동 검증 결과가 아닙니다.",
                    "실게임 결과 직접 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            string target = Path.Combine(directory, "user-feedback.json");
            string temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                event_id = selected.EventId, source = "manual_ui",
                verdict, confirmed_at = DateTimeOffset.UtcNow.ToString("O")
            }));
            File.Move(temp, target, true);
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { MessageBox.Show(this, "결과 기록 실패: 로컬 폴더 접근 또는 JSON 오류"); }
    }

    private void CopyChatGptHandoff()
    {
        // Clipboard is local and user-initiated. Never copy raw error.txt,
        // screenshots, token values, result diagnostics or absolute paths.
        using var manifest = ReadBounded(Path.Combine(Root, "chatgpt-handoff.json"));
        if (manifest is null || manifest.RootElement.ValueKind != JsonValueKind.Object)
        {
            MessageBox.Show(this, "현재 인계할 요약이 없습니다. AI 오류 대기 상태를 확인하세요.");
            return;
        }
        var root = manifest.RootElement;
        string state = Field(root, "status", 50);
        if (state is not ("codex_quota_exhausted" or "daily_limit") ||
            !root.TryGetProperty("includes_raw_logs", out var includesRaw) ||
            includesRaw.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("automatic_chatgpt_upload", out var autoUpload) ||
            autoUpload.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("items", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            MessageBox.Show(this, "검증된 인계 요약 형식이 아닙니다.");
            return;
        }
        string[] permitted =
        {
            "receipt_state", "cli_state", "visual_recognition",
            "scheduler_transition", "timing_stall", "input_control", "unclassified"
        };
        var summaries = new List<string>();
        foreach (var entry in entries.EnumerateArray().Take(20))
        {
            string id = Field(entry, "event_id", 40);
            if (id.Length != 16 || !id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                continue;
            string category = Field(entry, "suspected_category", 50);
            if (!permitted.Contains(category, StringComparer.Ordinal)) category = "unclassified";
            summaries.Add("- " + id + " · 추정 분류: " + category);
        }
        string prompt = "MabiAuto 스케줄러 오류를 이어서 분석해 주세요.\n" +
            "GitHub: skwlrna/Mbi_auto_update (현재 main 기준).\n" +
            "AI 중단 사유: " + state + " · 로컬 대기/검토 " + Number(root, "queued_total") + "건.\n" +
            string.Join("\n", summaries) +
            "\n오류 ID는 로컬 증거 식별자입니다. 원본 로그·스크린샷·토큰은 여기에 포함하지 않았습니다." +
            "\n필요한 경우 민감정보를 제거한 오류 로그나 화면은 사용자에게 따로 요청해 주세요." +
            "\nAI의 테스트 통과만으로 실게임 해결을 확정하거나 자동 배포하지 마세요.";
        try
        {
            Clipboard.SetText(prompt);
            MessageBox.Show(this, "원본 로그 없이 작성된 인계 요약을 복사했습니다. ChatGPT 대화에 직접 붙여넣으세요. 필요할 때만 검토한 로그를 추가하세요.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "클립보드에 복사하지 못했습니다. 다른 프로그램이 사용 중인지 확인하세요.");
        }
    }

    private void RequestQuotaResume()
    {
        using var activity = ReadBounded(Path.Combine(Root, "watch-activity.json"));
        if (activity is null || Field(activity.RootElement, "status") != "quota_paused" ||
            !_isWatcherRunning())
        {
            MessageBox.Show(this, "현재 실행 중인 Codex 한도 일시정지 상태가 아닙니다.");
            return;
        }
        string token = Field(activity.RootElement, "pause_id", 40);
        if (token.Length != 32 || !token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return;
        if (MessageBox.Show(this,
            "Codex 사용량 초기화 또는 크레딧 복구를 직접 확인했습니까?\n" +
            "확인 시 새 오류 분석을 재개할 수 있으며 Codex 사용량이 발생합니다.\n" +
            "이전에 차단된 수정안을 자동 재실행하지는 않습니다.",
            "Codex 호출 재개 확인", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            string destination = Path.Combine(Root, "quota-resume-request.json");
            string temp = destination + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                action = "retry_after_user_confirmed_reset",
                pause_id = token
            }));
            File.Move(temp, destination, true);
            MessageBox.Show(this, "AI 감시기에 재개 요청을 기록했습니다. 다음 감시 주기에 반영됩니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Codex 재개 요청 저장에 실패했습니다.");
        }
    }

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
            (activity == "quota_paused" ? " · Codex 사용량 소진: ChatGPT 인계 가능" : "") +
            (running ? " · " + activity : " · 이전 상태 기록은 현재 실행 상태가 아님");

        try
        {
            using (var memory = ReadBounded(Path.Combine(Root, "error-memory.json")))
            {
                int stored = 0, candidates = 0, confirmed = 0, recurred = 0;
                if (memory is not null &&
                    memory.RootElement.ValueKind == JsonValueKind.Object &&
                    memory.RootElement.TryGetProperty("entries", out var entries) &&
                    entries.ValueKind == JsonValueKind.Array)
                {
                    stored = entries.GetArrayLength();
                    foreach (var item in entries.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("verified_offline_tests", out var passed) &&
                            passed.ValueKind == JsonValueKind.True) candidates++;
                        string id = Field(item, "event_id");
                        if (ValidEventId(id))
                        {
                            string userVerdict = ManualVerdict(Path.Combine(Root, id), id);
                            if (userVerdict == "resolved") confirmed++;
                            if (userVerdict == "recurred") recurred++;
                        }
                    }
                }
                _memory.Text = $"AI 오류 이력 {stored}건 · 테스트 통과 후보 {candidates}건 · " +
                    $"실게임 해결 확인 {confirmed}건 · 재발 {recurred}건";
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
                string feedback = status == "tests_passed_review_required" && ValidEventId(eventId)
                    ? ManualVerdict(file.DirectoryName ?? "", eventId) : "unverified";
                int i = _recent.Rows.Add(eventId, StatusText(status), VerdictText(feedback), prText);
                _recent.Rows[i].Tag = new ResultItem(
                    IsTrustedPrUrl(url) ? url : "", file.FullName, eventId);
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
