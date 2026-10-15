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
    private readonly Label _selectedDetail;
    private readonly DataGridView _recent;
    private sealed record ResultItem(string Url, string FilePath, string EventId,
        string When, string Title, string Detail, string Status, string Next, string Reason);
    private sealed record LocalError(string Name, DateTimeOffset When, string Title, string Detail);
    private sealed record TimelineEntry(DateTimeOffset When, string Name, string Title, string Detail,
        string Status, string Next, string Pr, string Url, string FilePath, string EventId, string Reason);
    private readonly System.Windows.Forms.Timer _timer;
    private bool _quotaHandoffOffered;

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "MabiAutoAI", "autopilot-results");

    internal AiAutopilotDashboard(Func<bool> isWatcherRunning, Func<string> watcherStatus)
    {
        _isWatcherRunning = isWatcherRunning;
        _watcherStatus = watcherStatus;
        Text = "MabiAuto · AI 오류 기록";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(960, 550);
        ClientSize = new Size(1120, 670);
        BackColor = Color.FromArgb(246, 248, 252);
        Font = new Font("맑은 고딕", 9f);

        var heading = new Label
        {
            Text = "AI 오류 기록 · 자동수정 진행 현황", Dock = DockStyle.Top, Height = 49,
            Font = new Font("맑은 고딕", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(23, 32, 51),
            Padding = new Padding(17, 10, 0, 0)
        };
        _overview = new Label
        {
            Dock = DockStyle.Top, Height = 70, AutoEllipsis = true,
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
            Text = "이전 오류 기록을 확인하고 있습니다.",
            Dock = DockStyle.Top, Height = 34,
            ForeColor = Color.FromArgb(70, 89, 112),
            Padding = new Padding(18, 5, 12, 0)
        };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 51 };
        _updated = new Label
        {
            Text = "PC에 저장된 오류와 AI 처리 결과입니다. 자동 업데이트는 하지 않습니다.",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(82, 99, 120),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0)
        };
        var confirmed = new Button
        {
            Text = "적용 후 해결됨", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        confirmed.Click += (_, _) => SubmitGameFeedback("resolved");
        var recurred = new Button
        {
            Text = "적용 후 재발함", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        recurred.Click += (_, _) => SubmitGameFeedback("recurred");
        var reset = new Button
        {
            Text = "확인 취소", Dock = DockStyle.Right, Width = 93,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        reset.Click += (_, _) => SubmitGameFeedback("unverified");
        var handoff = new Button
        {
            Text = "한도 초과 시 ChatGPT", Dock = DockStyle.Right, Width = 137,
            BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        handoff.Click += (_, _) => CopyChatGptHandoff();
        var resumeCodex = new Button
        {
            Text = "한도 복구 후 재개", Dock = DockStyle.Right, Width = 125,
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
        _recent.ColumnHeadersHeight = 38;
        _recent.RowTemplate.Height = 34;
        _recent.Columns.Add("when", "발생 일시");
        _recent.Columns.Add("error", "무슨 오류인가요?");
        _recent.Columns.Add("status", "AI 처리 상태");
        _recent.Columns.Add("next", "다음 단계");
        _recent.Columns.Add("pr", "GitHub 수정안");
        _recent.Columns[0].FillWeight = 88;
        _recent.Columns[1].FillWeight = 242;
        _recent.Columns[2].FillWeight = 130;
        _recent.Columns[3].FillWeight = 175;
        _recent.Columns[4].FillWeight = 110;
        _recent.SelectionChanged += (_, _) => ShowSelectedDetail();
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
            Text = "최신 오류부터 표시합니다. 오류를 누르면 아래에 원인과 조치 방법이 나옵니다. GitHub 수정안은 더블클릭으로 열 수 있습니다.",
            Dock = DockStyle.Top, Height = 34, Padding = new Padding(18, 5, 0, 0),
            ForeColor = Color.FromArgb(82, 99, 120)
        };
        _selectedDetail = new Label
        {
            Dock = DockStyle.Bottom, Height = 112, AutoEllipsis = true,
            Text = "위 목록에서 오류를 선택하면 자세한 내용이 표시됩니다.",
            BackColor = Color.White, ForeColor = Color.FromArgb(36, 54, 76),
            BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(18, 9, 12, 7)
        };
        Controls.Add(_recent);
        Controls.Add(_selectedDetail);
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
        "tests_passed_review_required" => "코드 테스트 통과 · 적용 전",
        "in_progress" => "AI 분석 진행 중",
        "needs_review" => "AI 판단에 추가 확인 필요",
        "tests_failed" => "수정했지만 테스트 실패",
        "blocked" => "자동수정 중단 (원인 확인)",
        "duplicate_skipped" => "같은 오류 중복 분석 생략",
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
            try
            {
                Process.Start(new ProcessStartInfo("https://chatgpt.com/")
                { UseShellExecute = true });
                MessageBox.Show(this, "안전한 오류 요약을 복사하고 ChatGPT를 열었습니다. 대화에 붙여넣어 전송하세요. 원본 로그와 비밀정보는 자동 전송하지 않습니다.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, "오류 요약은 복사됐습니다. ChatGPT를 직접 열어 붙여넣으세요.");
            }
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

    private static string DisplayText(string raw, int max)
    {
        return new string((raw ?? "").Where(ch => !char.IsControl(ch)).Take(max).ToArray()).Trim();
    }

    private static string ErrorLabel(string title, string detail)
    {
        if (detail.Contains("어비스") && detail.Contains("HUD"))
            return "어비스 퇴장 후 화면 확인 실패";
        if (detail.Contains("생활 스킬 100회") && detail.Contains("진행 불가"))
            return "채집 중 캐릭터 상태로 작업 중단";
        if (detail.Contains("클리어") && detail.Contains("확인"))
            return "어비스 클리어 화면 확인 실패";
        return !string.IsNullOrWhiteSpace(detail) ? DisplayText(detail, 100) :
            !string.IsNullOrWhiteSpace(title) ? title : "기존 오류 기록 (상세 정보 없음)";
    }

    private static string ExplainReason(string reason)
    {
        if (reason.Contains("codex_quota_exhausted")) return "Codex 사용량 한도로 분석이 중지됐습니다.";
        if (reason.Contains("codex_not_found")) return "PC에서 Codex 프로그램을 찾지 못했습니다.";
        if (reason.Contains("repo_dirty_or_git_error")) return "코드 저장소에 미저장 변경 사항이 있거나 Git 검사에 실패했습니다.";
        if (reason.Contains("worktree_retention_limit")) return "이전 AI 작업 기록이 최대 보관 개수에 도달했습니다.";
        if (reason.Contains("worktree_exists")) return "이전 작업이 남아 있어 같은 오류를 자동 재실행하지 않습니다.";
        if (reason.Contains("agent_no_patch")) return "AI가 안전하게 바꿀 코드를 찾지 못했습니다.";
        if (reason.Contains("regression_test_missing")) return "이 오류를 재현하는 자동 테스트가 없어 수정 검증을 보류했습니다.";
        if (reason.Contains("build_or_regression_test_failed")) return "수정안이 빌드 또는 자동 테스트를 통과하지 못했습니다.";
        if (reason.Contains("autopilot_already_running")) return "다른 AI 감시기가 이미 실행 중입니다.";
        return string.IsNullOrWhiteSpace(reason) ? "아직 분석 결과가 없습니다." :
            "진단 코드: " + DisplayText(reason, 125);
    }

    private static string NextStep(string status, string reason)
    {
        if (reason.Contains("codex_quota_exhausted")) return "Codex 한도 복구 또는 ChatGPT 인계";
        if (reason.Contains("codex_not_found")) return "Codex 설치 및 로그인 확인";
        if (reason.Contains("repo_dirty_or_git_error")) return "Git 변경 상태 확인";
        return status switch
        {
            "tests_passed_review_required" => "GitHub 수정안 검토 → 적용 후 게임 확인",
            "in_progress" => "AI 분석 완료 여부 확인",
            "needs_review" => "오류 증거 또는 수정 코드 확인",
            "tests_failed" => "실패한 테스트 원인 확인",
            "blocked" => "중단 사유 확인",
            "duplicate_skipped" => "앞선 동일 오류 결과 확인",
            _ => "AI 감시가 켜져 있는지 확인"
        };
    }

    private static List<LocalError> ReadLocalErrors()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, "MabiAutoMCP", "reports"),
            Path.Combine(home, "MabiAutoAI", "MCP", "reports"),
            Path.Combine(home, "MabiAutoMCP", "report")
        };
        var results = new List<LocalError>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in Directory.EnumerateDirectories(root, "error_*")
                             .OrderByDescending(x => x, StringComparer.Ordinal).Take(300))
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    string name = Path.GetFileName(folder);
                    if (!names.Add(name)) continue;
                    using var summary = ReadBounded(Path.Combine(folder, "summary.json"));
                    if (summary is null) continue;
                    string timeText = Field(summary.RootElement, "timeLocal", 90);
                    if (!DateTimeOffset.TryParse(timeText, out var when)) continue;
                    string title = DisplayText(Field(summary.RootElement, "title", 160), 140);
                    string detail = DisplayText(Field(summary.RootElement, "detail", 450), 400);
                    results.Add(new LocalError(name, when, title, detail));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A broken local report never blocks viewing healthy reports.
            }
        }
        return results;
    }

    private void ShowSelectedDetail()
    {
        if (_recent.CurrentRow?.Tag is not ResultItem selected)
        {
            _selectedDetail.Text = "오류를 선택하면 상세 내용과 필요한 조치가 표시됩니다.";
            return;
        }
        _selectedDetail.Text = "발생: " + selected.When + "    오류: " + selected.Title +
            "\n내용: " + (string.IsNullOrWhiteSpace(selected.Detail) ? "세부 설명이 기록되지 않았습니다." : selected.Detail) +
            "\nAI 상태: " + selected.Status + "    다음 단계: " + selected.Next +
            (string.IsNullOrWhiteSpace(selected.Reason) ? "" : "\n중단/판단 사유: " + ExplainReason(selected.Reason));
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
        string activityLabel = activity switch
        {
            "watching" => "새 오류 감시 중",
            "processing" => "오류를 분석하고 수정안 테스트 중",
            "quota_paused" => "Codex 사용량 한도로 일시정지",
            "safe_stop" => "안전 검사에서 중지됨",
            _ => "아직 처리 상태가 기록되지 않음"
        };
        _overview.Text = "AI 자동수정: " + (running ? "켜짐" : "꺼짐") +
            "     |     진행 상태: " + (running ? activityLabel : _watcherStatus()) +
            "\n현재 처리 오류: " + (active.Length > 0 ? "아래 목록에서 확인" : "없음") +
            "     |     분석 대기: " + pending + "건" +
            (activity == "quota_paused" ? "     |     한도 복구 후 직접 재개" : "") +
            (running ? "" : "     |     표시된 분석 결과는 이전 기록일 수 있습니다.");

        if (running && activity == "quota_paused" && !_quotaHandoffOffered)
        {
            // Offer the handoff once per dashboard opening. No automatic
            // message submission, account introspection, or private log upload.
            _quotaHandoffOffered = true;
            if (MessageBox.Show(this,
                "Codex 사용량 한도에 도달했습니다. 추가 호출은 중지됐고 오류 기록은 보존됩니다.\n" +
                "ChatGPT에서 계속 검토할까요? 안전한 오류 요약만 복사해 브라우저를 엽니다.",
                "ChatGPT 인계 준비 완료", MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) == DialogResult.Yes)
                CopyChatGptHandoff();
        }
        if (activity != "quota_paused") _quotaHandoffOffered = false;

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
            var local = ReadLocalErrors();
            var byName = local.GroupBy(x => x.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var represented = new HashSet<string>(StringComparer.Ordinal);
            var timeline = new List<TimelineEntry>();
            int success = 0, failed = 0, review = 0, activeCount = 0, createdPr = 0;
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
                        prText = "수정안 #" + url[(url.LastIndexOf('/') + 1)..];
                    }
                    else if (stateName == "blocked") prText = "등록 보류";
                    else if (stateName == "disabled") prText = "자동 등록 꺼짐";
                }
                string id = Field(root, "event_id", 100);
                if (id.Length == 0) id = file.Directory?.Name ?? "기록";
                string name = Field(root, "event", 160);
                if (!string.IsNullOrWhiteSpace(name)) represented.Add(name);
                string reason = Field(root, "reason", 300);
                string feedback = status == "tests_passed_review_required" && ValidEventId(id)
                    ? ManualVerdict(file.DirectoryName ?? "", id) : "unverified";
                string visibleStatus = StatusText(status);
                if (status == "in_progress" && (!running || active != name))
                    visibleStatus = "이전 분석이 완료되지 않음";
                if (feedback == "resolved") visibleStatus += " · 게임 해결 확인";
                if (feedback == "recurred") visibleStatus += " · 게임 재발 확인";
                string next = NextStep(status, reason);
                string title = "기존 오류 기록";
                string detail = "";
                DateTimeOffset when = new DateTimeOffset(file.LastWriteTime);
                if (DateTimeOffset.TryParse(Field(root, "started", 90), out var recorded))
                    when = recorded;
                if (byName.TryGetValue(name, out var source))
                {
                    title = source.Title;
                    detail = source.Detail;
                    when = source.When;
                }
                timeline.Add(new TimelineEntry(when, name, title, detail,
                    visibleStatus, next, prText, IsTrustedPrUrl(url) ? url : "",
                    file.FullName, id, reason));
            }
            foreach (var item in local)
            {
                if (represented.Contains(item.Name)) continue;
                bool inProgress = running && activity == "processing" && active == item.Name;
                timeline.Add(new TimelineEntry(item.When, item.Name, item.Title, item.Detail,
                    inProgress ? "AI가 오류 분석 중" : "AI 분석 기록 없음",
                    inProgress ? "자동수정 결과 확인" :
                        running ? "현재 오류가 분석 대기 중인지 확인" : "AI 감시 켜기 · 다음 오류부터 분석",
                    "없음", "", "", "", ""));
            }
            _recent.Rows.Clear();
            foreach (var item in timeline.OrderByDescending(x => x.When).Take(40))
            {
                string whenText = item.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                int i = _recent.Rows.Add(whenText, ErrorLabel(item.Title, item.Detail),
                    item.Status, item.Next, item.Pr);
                _recent.Rows[i].Tag = new ResultItem(item.Url, item.FilePath, item.EventId,
                    whenText, item.Title, item.Detail, item.Status, item.Next, item.Reason);
                _recent.Rows[i].Cells[1].ToolTipText = item.Detail;
                _recent.Rows[i].Cells[2].ToolTipText = ExplainReason(item.Reason);
            }
            ShowSelectedDetail();
            _counts.Text = "오류 기록 " + timeline.Count + "건  |  AI 테스트 통과 " + success +
                "건  |  수정 실패·중단 " + failed + "건  |  검토 필요 " + review +
                "건  |  분석 미완료 " + activeCount + "건  |  GitHub 수정안 " + createdPr + "건";
            _updated.Text = "최근 갱신 " + DateTime.Now.ToString("HH:mm:ss") +
                " · 시간순 정렬 · PC 로컬 오류 기록 기준 · 게임에서 해결됐는지는 별도 확인 필요";        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _updated.Text = "AI 분석 결과 폴더를 읽지 못했습니다.";
        }
    }
}
