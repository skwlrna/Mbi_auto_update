using System.Diagnostics;
using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// Read-only local snapshot of the existing Autopilot result directory.
/// No process control, script invocation, upload or automatic PR operations.
/// </summary>
internal sealed class AiAutopilotDashboard : Form
{
    private static readonly Color UiBackground = Color.FromArgb(23, 23, 24);
    private static readonly Color UiPanel = Color.FromArgb(30, 30, 32);
    private static readonly Color UiButton = Color.FromArgb(38, 38, 40);
    private static readonly Color UiText = Color.FromArgb(238, 238, 244);
    private static readonly Color UiMuted = Color.FromArgb(181, 185, 196);
    private static readonly Color UiBorder = Color.FromArgb(53, 53, 57);
    private static readonly Color UiAccentSoft = Color.FromArgb(54, 44, 74);
    private static readonly Color UiError = Color.FromArgb(255, 159, 151);

    private readonly Func<bool> _isWatcherRunning;
    private readonly Func<string> _watcherStatus;
    private readonly Label _overview;
    private readonly Label _pipeline;
    private readonly Label _counts;
    private readonly Label _updated;
    private readonly Label _memory;
    private readonly DataGridView _recent;
    private readonly Label _details;
    private readonly ComboBox _range;
    private sealed record ResultItem(string Url, string FilePath, string EventId,
        string Title, string Detail, string Status, string Next);
    private sealed record Analysis(string EventName, string EventId, string FilePath,
        DateTimeOffset Time, string Status, string Reason, string Url, string PrText,
        string Feedback, string Provenance);
    private readonly System.Windows.Forms.Timer _timer;
    private bool _quotaHandoffOffered;

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "MabiAutoAI", "autopilot-results");

    internal AiAutopilotDashboard(Func<bool> isWatcherRunning, Func<string> watcherStatus)
    {
        _isWatcherRunning = isWatcherRunning;
        _watcherStatus = watcherStatus;
        Text = "MabiAuto · AI 오류 시간별 내역";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1080, 550);
        ClientSize = new Size(1140, 720);
        BackColor = UiBackground;
        ForeColor = UiText;
        Font = new Font("맑은 고딕", 9f);

        var heading = new Label
        {
            Text = "AI 오류 내역 · 발생 시간순", Dock = DockStyle.Top, Height = 49,
            Font = new Font("맑은 고딕", 14f, FontStyle.Bold),
            ForeColor = UiText,
            Padding = new Padding(17, 10, 0, 0)
        };
        _overview = new Label
        {
            Dock = DockStyle.Top, Height = 76, AutoEllipsis = true,
            ForeColor = UiMuted,
            Padding = new Padding(18, 6, 12, 0)
        };
        _pipeline = new Label
        {
            Dock = DockStyle.Top, Height = 72, AutoEllipsis = true,
            ForeColor = UiText, BackColor = UiPanel,
            Font = new Font("맑은 고딕", 9f),
            Padding = new Padding(18, 8, 10, 4),
            Text = "AI 단계별 현황: 오류 대기 중"
        };
        _counts = new Label
        {
            Dock = DockStyle.Top, Height = 54,
            ForeColor = UiText,
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Padding = new Padding(18, 5, 12, 0)
        };
        _memory = new Label
        {
            Text = "초기 후보 로컬 테스트 ≠ 최종 PR CI ≠ 실게임 해결. 세 단계는 별개입니다.",
            Dock = DockStyle.Top, Height = 34,
            ForeColor = UiMuted,
            Padding = new Padding(18, 5, 12, 0)
        };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 51, BackColor = UiBackground };
        _updated = new Label
        {
            Text = "최근 기록을 표시합니다 · 게임에 자동 적용하지 않습니다.",
            Dock = DockStyle.Fill, ForeColor = UiMuted,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0)
        };
        var confirmed = new Button
        {
            Text = "적용 후 해결 확인", Dock = DockStyle.Right, Width = 137,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        confirmed.Click += (_, _) => SubmitGameFeedback("resolved");
        var recurred = new Button
        {
            Text = "적용 후 재발", Dock = DockStyle.Right, Width = 137,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        recurred.Click += (_, _) => SubmitGameFeedback("recurred");
        var reset = new Button
        {
            Text = "확인 취소", Dock = DockStyle.Right, Width = 93,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        reset.Click += (_, _) => SubmitGameFeedback("unverified");
        var handoff = new Button
        {
            Text = "ChatGPT로 이어가기", Dock = DockStyle.Right, Width = 137,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        handoff.Click += (_, _) => CopyChatGptHandoff();
        var resumeCodex = new Button
        {
            Text = "Codex 재개", Dock = DockStyle.Right, Width = 125,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        resumeCodex.Click += (_, _) => RequestQuotaResume();
        var retryInterrupted = new Button
        {
            Text = "중단 작업 재분석", Dock = DockStyle.Right, Width = 140,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        retryInterrupted.Click += (_, _) => RequestInterruptedRetry();
        var retryQuota = new Button
        {
            Text = "한도 오류 재분석", Dock = DockStyle.Right, Width = 130,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        retryQuota.Click += (_, _) => RequestQuotaRetry();
        var refresh = new Button
        {
            Text = "새로고침", Dock = DockStyle.Right, Width = 105,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        foreach (var button in new[] { confirmed, recurred, reset, handoff, resumeCodex,
            retryInterrupted, retryQuota, refresh })
        {
            button.FlatAppearance.BorderColor = UiBorder;
            button.FlatAppearance.MouseOverBackColor = UiAccentSoft;
        }
        refresh.Click += (_, _) => Reload();
        foot.Controls.Add(_updated);
        foot.Controls.Add(refresh);
        foot.Controls.Add(handoff);
        foot.Controls.Add(resumeCodex);
        foot.Controls.Add(retryInterrupted);
        foot.Controls.Add(retryQuota);
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
            BackgroundColor = UiBackground, GridColor = UiBorder
        };
        _recent.EnableHeadersVisualStyles = false;
        _recent.BackColor = UiPanel;
        _recent.ForeColor = UiText;
        _recent.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPanel, ForeColor = UiText,
            SelectionBackColor = UiAccentSoft, SelectionForeColor = UiText
        };
        _recent.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiButton, ForeColor = UiText,
            SelectionBackColor = UiAccentSoft, SelectionForeColor = UiText
        };
        _recent.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiButton, ForeColor = UiText,
            SelectionBackColor = UiButton, SelectionForeColor = UiText
        };
        _recent.RowHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiButton, ForeColor = UiText,
            SelectionBackColor = UiAccentSoft, SelectionForeColor = UiText
        };
        _recent.ColumnHeadersHeight = 39;
        _recent.RowTemplate.Height = 35;
        _recent.Columns.Add("occurred", "발생 날짜·시간");
        _recent.Columns.Add("title", "어떤 오류?");
        _recent.Columns.Add("detail", "상세 내용");
        _recent.Columns.Add("status", "AI가 한 일");
        _recent.Columns.Add("action", "다음 단계");
        _recent.Columns.Add("pr", "수정안");
        _recent.Columns[0].FillWeight = 111;
        _recent.Columns[1].FillWeight = 130;
        _recent.Columns[2].FillWeight = 265;
        _recent.Columns[3].FillWeight = 123;
        _recent.Columns[4].FillWeight = 151;
        _recent.Columns[5].FillWeight = 82;
        _recent.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _recent.Rows.Count) return;
            if (_recent.Rows[e.RowIndex].Tag is not ResultItem item) return;
            if (!IsTrustedPrUrl(item.Url))
            {
                MessageBox.Show(this, "어떤 오류인가요?\n" + item.Title +
                    "\n\n발생 내용\n" + item.Detail +
                    "\n\nAI 처리\n" + item.Status +
                    "\n\n다음 단계\n" + item.Next,
                    "오류 자세히 보기", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true }); }
            catch (Exception) { /* External browser is optional. */ }
        };

        _details = new Label
        {
            Text = "오류를 선택하면 원인과 다음에 할 일을 아래에서 볼 수 있습니다.",
            Dock = DockStyle.Bottom, Height = 90,
            ForeColor = UiText,
            BackColor = UiPanel, BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(15, 7, 10, 6), AutoEllipsis = true
        };
        _recent.SelectionChanged += (_, _) =>
        {
            if (_recent.CurrentRow?.Tag is ResultItem item)
                _details.Text = "오류: " + item.Title + "\n내용: " + item.Detail +
                    "\nAI 처리: " + item.Status + "   |   다음 단계: " + item.Next;
        };
        var intro = new Label
        {
            Text = "최신 오류가 위에 나옵니다. 한 줄을 누르면 자세히 볼 수 있고, 수정안이 있으면 두 번 눌러 GitHub에서 엽니다.",
            Dock = DockStyle.Top, Height = 30, Padding = new Padding(18, 0, 0, 0),
            ForeColor = UiMuted
        };
        var rangeBar = new Panel { Dock = DockStyle.Top, Height = 39, BackColor = UiBackground };
        _range = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 140,
            BackColor = UiButton, ForeColor = UiText, FlatStyle = FlatStyle.Flat,
            Location = new Point(18, 6), Font = new Font("맑은 고딕", 9f)
        };
        _range.Items.AddRange(new object[] { "전체 내역", "오늘", "최근 7일" });
        _range.SelectedIndex = 0;
        _range.SelectedIndexChanged += (_, _) => Reload();
        var explanation = new Label
        {
            Text = "발생 시간 기준 · PC에 기록된 오류와 Codex 분석 결과",
            Location = new Point(175, 8), AutoSize = true,
            ForeColor = UiMuted
        };
        var checkPipeline = new Button
        {
            Text = "AI 사전 점검 (실행 없음)", Dock = DockStyle.Right, Width = 195,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        checkPipeline.Click += async (_, _) => await RequestPipelineCheckAsync();
        var retentionCleanup = new Button
        {
            Text = "작업공간 안전 정리", Dock = DockStyle.Right, Width = 155,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        retentionCleanup.Click += (_, _) => RequestRetentionCleanup();
        var retentionRetry = new Button
        {
            Text = "공간 한도 재분석", Dock = DockStyle.Right, Width = 145,
            BackColor = UiButton, ForeColor = UiText, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        retentionRetry.Click += (_, _) => RequestRetentionRetry();
        foreach (var button in new[] { checkPipeline, retentionCleanup, retentionRetry })
        {
            button.FlatAppearance.BorderColor = UiBorder;
            button.FlatAppearance.MouseOverBackColor = UiAccentSoft;
        }
        rangeBar.Controls.Add(checkPipeline);
        rangeBar.Controls.Add(retentionCleanup);
        rangeBar.Controls.Add(retentionRetry);
        rangeBar.Controls.Add(_range);
        rangeBar.Controls.Add(explanation);
        Controls.Add(_recent);
        Controls.Add(_details);
        Controls.Add(intro);
        Controls.Add(foot);
        Controls.Add(_memory);
        Controls.Add(_counts);
        Controls.Add(_pipeline);
        Controls.Add(_overview);
        Controls.Add(rangeBar);
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
        "tests_passed_review_required" => "초기 후보 로컬 테스트 통과",
        "in_progress" => "AI 분석 중 또는 강제 종료 전 기록",
        "interrupted_review_required" => "이전 AI 분석 중단 · 재분석 가능",
        "needs_review" => "수정안 검토 필요",
        "tests_failed" => "수정 후 테스트 실패",
        "blocked" => "분석/수정 중단",
        "duplicate_skipped" => "중복 오류 · 기록 보존",
        _ => "상태 확인 필요"
    };

    private static string FriendlyError(string title, string detail)
    {
        if (detail.Contains("어비스", StringComparison.OrdinalIgnoreCase) &&
            (detail.Contains("퇴장") || detail.Contains("나가기") || detail.Contains("HUD")))
            return "어비스 퇴장·화면 확인 실패";
        if (detail.Contains("생활 스킬") && detail.Contains("진행 불가"))
            return "채집 중 캐릭터 상태 이상";
        if (detail.Contains("채집") && detail.Contains("수량"))
            return "채집 수량 확인 오류";
        if (detail.Contains("재료") && detail.Contains("부족"))
            return "가공 재료 부족";
        if (detail.Contains("OCR") || detail.Contains("인식 실패"))
            return "게임 화면 인식 실패";
        return title;
    }

    private static DateTimeOffset TimeOf(string value, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : fallback;

    private static string ReasonText(string status, string reason)
    {
        if (reason.Contains("macro_or_watcher_exited_during_isolated_fix", StringComparison.OrdinalIgnoreCase))
            return "매크로 또는 AI 종료로 코드 수정이 중간에 중단됨. 이전 작업은 보존됨";
        if (reason.Contains("codex_quota_exhausted", StringComparison.OrdinalIgnoreCase))
            return "Codex 사용량 한도로 중단";
        if (reason.Contains("codex_not_found", StringComparison.OrdinalIgnoreCase))
            return "Codex 프로그램을 찾지 못함";
        if (reason.Contains("repo_dirty_or_git_error", StringComparison.OrdinalIgnoreCase))
            return "작업용 소스에 저장되지 않은 변경 사항이 있음";
        if (reason.Contains("legacy_autofix", StringComparison.OrdinalIgnoreCase))
            return "구버전 자동수정 검사로 차단된 과거 기록";
        if (reason.Contains("regression_test_missing", StringComparison.OrdinalIgnoreCase))
            return "오류를 확인할 테스트 코드가 부족함";
        if (reason.Contains("agent_no_patch", StringComparison.OrdinalIgnoreCase))
            return "AI가 수정할 코드를 찾지 못함";
        if (reason.Contains("worktree_retention_limit", StringComparison.OrdinalIgnoreCase))
            return "AI 작업 공간이 가득 참 · 기존 자료 보존, 공간 정리 필요";
        if (status == "tests_failed") return "코드를 수정했지만 자동 테스트에 실패함";
        if (status == "blocked") return "AI 분석 또는 안전 검사를 통과하지 못함";
        if (status == "needs_review") return "검증 근거가 부족하여 사람이 확인해야 함";
        return "AI 처리 기록";
    }

    private static string NextStep(string status, string reason, bool running) => status switch
    {
        "tests_passed_review_required" => "PR 최종 CI 및 실게임 별도 확인",
        "in_progress" => running ? "현재 분석인지 위의 감시 상태 확인" : "AI 재시작 시 중단 상태 판정",
        "interrupted_review_required" => running ? "해당 오류 선택 → 중단 작업 재분석" : "AI 감시 시작 → 중단 작업 재분석",
        "needs_review" => "수정안·오류 증거 검토",
        "tests_failed" => "실패한 테스트 기록 확인",
        "blocked" when reason.Contains("worktree_retention_limit_reached", StringComparison.OrdinalIgnoreCase) =>
            running ? "안전 정리 후 공간 한도 재분석" : "AI 감시 시작 → 안전 정리 후 재분석",
        "blocked" when reason.Contains("codex_quota_exhausted", StringComparison.OrdinalIgnoreCase) =>
            running ? "한도 복구 확인 후 한도 오류 재분석" : "AI 시작 → Codex 재개 → 한도 오류 재분석",
        "blocked" => "차단 이유를 확인하고 환경 점검",
        "duplicate_skipped" => "앞선 동일 오류 분석 기록 확인",
        "not_started" => running ? "AI가 순서대로 분석 예정" : "AI 감시 시작 필요",
        _ => "오류 기록 확인"
    };

    private static bool ValidEventId(string id)
    {
        if (id.Length is not (16 or 35)) return false;
        string prefix = id.Length == 16 ? id : id[..16];
        return prefix.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            (id.Length == 16 || (id == prefix + "-retry-cli-arg-v121" || id == prefix + "-retry-retention-v1"));
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

    private void RequestRetentionCleanup()
    {
        if (!_isWatcherRunning())
        {
            MessageBox.Show(this, "AI 감시를 켠 후 작업 공간 보관 상태를 확인할 수 있습니다.");
            return;
        }
        try
        {
            string activityPath = Path.Combine(Root, "watch-activity.json");
            using var activity = ReadBounded(activityPath);
            if (activity is null || Field(activity.RootElement, "status") != "watching")
            {
                MessageBox.Show(this, "AI 작업 진행 중에는 정리할 수 없습니다. 작업이 끝날 때까지 기다려 주세요.");
                return;
            }
            string inventoryPath = Path.Combine(Root, "worktree-retention.json");
            using var inventory = ReadBounded(inventoryPath);
            if (inventory is null || Field(inventory.RootElement, "kind") != "retention_read_only" ||
                File.GetLastWriteTimeUtc(inventoryPath) < DateTime.UtcNow.AddMinutes(-2) ||
                !inventory.RootElement.TryGetProperty("archive_ready_candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array)
            {
                MessageBox.Show(this, "작업공간 현황이 없습니다. AI 감시를 켜고 잠시 기다린 뒤 다시 확인하세요.");
                return;
            }
            var verified = candidates.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(x => ValidEventId(x)).ToArray();
            int count = Number(inventory.RootElement, "count");
            int limit = Number(inventory.RootElement, "limit");
            int archiveConflicts = inventory.RootElement.TryGetProperty("archive_conflicts", out var conflicts) &&
                conflicts.ValueKind == JsonValueKind.Array ? conflicts.GetArrayLength() : 0;
            if (verified.Length == 0)
            {
                MessageBox.Show(this, "보관 공간 " + count + "/" + limit +
                    " · 안전 정리 가능한 새 후보가 없습니다. 검토 중인 수정안은 보호합니다. " +
                    (archiveConflicts > 0 ? "기존 ZIP/임시 백업 이름 충돌 " + archiveConflicts +
                        "건 · 해당 백업은 덮어쓰지 않습니다. " : "") +
                    "강제로 삭제하지 말고 작업 폴더와 결과를 별도 검토하세요.");
                return;
            }
            string id = verified[0];
            if (MessageBox.Show(this, "보관 공간 " + count + "/" + limit +
                "\n안전 정리 후보 " + verified.Length + "건 중 1건만 처리합니다." +
                "\n오류 ID: " + id +
                "\n\nCodex 작업 폴더 전체를 ZIP으로 백업하고 해시 검증한 후, " +
                "Git에 등록된 깨끗한 완료 작업 폴더만 제거합니다." +
                "\n원본 오류·수정 결과·Git 브랜치는 유지합니다." +
                "\n백업 실패나 코드 변경 감지 시 삭제하지 않습니다. 계속할까요?",
                "작업 공간 1건 정리 승인", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string request = Path.Combine(Root, "retention-clean-request.json");
            if (File.Exists(request))
            {
                MessageBox.Show(this, "기존 정리 요청이 대기 중입니다. 다음 감시 주기를 기다려 주세요.");
                return;
            }
            File.WriteAllText(request + ".tmp", JsonSerializer.Serialize(new {
                action = "archive_clean_worktree_once", event_id = id
            }));
            File.Move(request + ".tmp", request, false);
            MessageBox.Show(this, "정리 1건을 요청했습니다. 다음 감시 주기에 재검증·백업 후 처리합니다. " +
                "결과는 AI 로그 및 작업 공간 현황에서 확인하세요.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "안전 정리 요청을 저장하지 못했습니다. 원본 작업 공간은 보존됩니다.");
        }
    }

    private void RequestRetentionRetry()
    {
        if (_recent.CurrentRow?.Tag is not ResultItem selected ||
            selected.EventId.Length != 16 || !ValidEventId(selected.EventId))
        {
            MessageBox.Show(this, "보관 한도로 차단된 오류 행을 먼저 선택하세요.");
            return;
        }
        if (!_isWatcherRunning())
        {
            MessageBox.Show(this, "AI 감시를 시작한 뒤 재분석을 요청하세요.");
            return;
        }
        try
        {
            string originalPath = Path.Combine(Root, selected.EventId, "result.json");
            if (!string.Equals(originalPath, selected.FilePath, StringComparison.OrdinalIgnoreCase))
                return;
            using var original = ReadBounded(originalPath);
            using var inventory = ReadBounded(Path.Combine(Root, "worktree-retention.json"));
            if (original is null || inventory is null ||
                Field(original.RootElement, "event_id") != selected.EventId ||
                Field(original.RootElement, "status") != "blocked" ||
                Field(original.RootElement, "reason") != "worktree_retention_limit_reached")
            {
                MessageBox.Show(this, "작업 공간 보관 한도로 차단된 원본 오류에만 사용할 수 있습니다.");
                return;
            }
            if (Number(inventory.RootElement, "count") >= Number(inventory.RootElement, "limit") ||
                Number(inventory.RootElement, "limit") == 0)
            {
                MessageBox.Show(this, "작업 공간 한도가 해소되지 않았습니다. 먼저 안전 정리를 완료하세요.");
                return;
            }
            if (MessageBox.Show(this, "기존 오류 결과는 변경하지 않고 새 작업공간에서 1회 다시 분석합니다." +
                "\nCodex 사용량이 발생하며, 수정안을 게임에 자동 적용하지 않습니다. 계속할까요?",
                "공간 한도 오류 1회 재분석", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string request = Path.Combine(Root, "retention-retry-request.json");
            if (File.Exists(request) || Directory.Exists(Path.Combine(Root,
                selected.EventId + "-retry-retention-v1")))
            {
                MessageBox.Show(this, "이 오류에 대한 재분석이 이미 요청됐거나 실행됐습니다.");
                return;
            }
            File.WriteAllText(request + ".tmp", JsonSerializer.Serialize(new {
                action = "retry_retention_blocked_once", event_id = selected.EventId
            }));
            File.Move(request + ".tmp", request, false);
            MessageBox.Show(this, "재분석을 요청했습니다. 다음 AI 감시 주기에 처리합니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "재분석 요청 저장에 실패했습니다. 원본 결과는 보존됩니다.");
        }
    }

    private void RequestInterruptedRetry()
    {
        if (_recent.CurrentRow?.Tag is not ResultItem selected ||
            !ValidEventId(selected.EventId) || selected.EventId.Length != 16)
        {
            MessageBox.Show(this, "중단된 AI 오류를 먼저 선택하세요.");
            return;
        }
        if (!_isWatcherRunning())
        {
            MessageBox.Show(this, "AI 감시를 켠 후 재분석할 수 있습니다. 매크로 종료 시 감시도 함께 종료됩니다.");
            return;
        }
        try
        {
            string directory = Path.Combine(Root, selected.EventId);
            string resultPath = Path.Combine(directory, "result.json");
            if (!string.Equals(selected.FilePath, resultPath, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return;
            using var result = ReadBounded(resultPath);
            if (result is null ||
                Field(result.RootElement, "event_id", 40) != selected.EventId ||
                Field(result.RootElement, "status", 64) != "interrupted_review_required")
            {
                MessageBox.Show(this, "완전히 중단된 AI 작업만 새 작업 공간에서 재분석할 수 있습니다. 현재 분석 중인 작업은 재시작하지 않습니다.");
                return;
            }
            string request = Path.Combine(Root, "interrupted-retry-request.json");
            if (File.Exists(request))
            {
                MessageBox.Show(this, "기존 재분석 요청이 있습니다. 처리 상태를 먼저 확인하세요.");
                return;
            }
            if (MessageBox.Show(this,
                "이전에 중단된 수정안은 그대로 보관됩니다. 같은 오류 증거를 새 격리 작업 공간에서 Codex에 다시 보내 분석할까요?\n" +
                "Codex 사용량이 발생하며, 실제 게임에 수정안을 자동 적용하지 않습니다.",
                "AI 재분석 명시적 승인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes) return;
            string temp = request + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                action = "retry_interrupted_once",
                event_id = selected.EventId
            }));
            File.Move(temp, request, false);
            MessageBox.Show(this, "재분석을 요청했습니다. 다음 감시 주기에 새 작업 공간으로 처리합니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "재분석 요청을 저장하지 못했습니다. 기존 결과는 그대로 유지됩니다.");
        }
    }

    private void RequestQuotaRetry()
    {
        if (_recent.CurrentRow?.Tag is not ResultItem selected ||
            selected.EventId.Length != 16 || !ValidEventId(selected.EventId))
        {
            MessageBox.Show(this, "Codex 사용량 한도로 차단됐던 오류를 먼저 선택하세요.");
            return;
        }
        if (!_isWatcherRunning())
        {
            MessageBox.Show(this, "AI 감시를 시작하고 Codex 한도가 복구됐는지 확인한 후 사용하세요.");
            return;
        }
        using (var activity = ReadBounded(Path.Combine(Root, "watch-activity.json")))
        {
            if (activity is null || Field(activity.RootElement, "status") == "quota_paused")
            {
                MessageBox.Show(this, "Codex 사용량 일시정지 중입니다. Codex 재개가 확인된 후 요청하세요.");
                return;
            }
        }
        try
        {
            string directory = Path.Combine(Root, selected.EventId);
            string source = Path.Combine(directory, "result.json");
            if (!string.Equals(selected.FilePath, source, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return;
            using var report = ReadBounded(source);
            if (report is null ||
                Field(report.RootElement, "event_id", 40) != selected.EventId ||
                Field(report.RootElement, "status", 64) != "blocked" ||
                !Field(report.RootElement, "reason", 500).Contains("codex_quota_exhausted",
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "사용량 한도로 실제 차단된 원본 오류만 재분석할 수 있습니다.");
                return;
            }
            string request = Path.Combine(Root, "quota-retry-request.json");
            if (File.Exists(request))
            {
                MessageBox.Show(this, "이미 재분석 요청이 대기 중입니다. AI 처리 상태를 확인하세요.");
                return;
            }
            if (MessageBox.Show(this,
                "Codex 사용량이 실제로 복구됐습니까?\n" +
                "선택한 오류의 기존 분석·수정 파일은 보존하고 새 격리 작업 공간에서 단 한 번 재분석합니다.\n" +
                "Codex 사용량이 발생하며 게임 코드에 자동 적용·배포하지 않습니다.",
                "사용량 복구 후 수동 재분석 승인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            string temp = request + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                action = "retry_quota_blocked_once", event_id = selected.EventId
            }));
            File.Move(temp, request, false);
            MessageBox.Show(this, "한도 차단 오류 1회 재분석을 요청했습니다. 다음 AI 감시 주기에 처리됩니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "재분석 요청 저장 실패 · 기존 오류 결과는 유지됩니다.");
        }
    }

    private async Task RequestPipelineCheckAsync()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "ai-autopilot");
        string script = Path.Combine(directory, "autopilot.py");
        if (!File.Exists(script))
        {
            MessageBox.Show(this, "AI 모듈이 없습니다. 설치 패키지를 확인하세요.");
            return;
        }
        string python = Environment.GetEnvironmentVariable("MABIAUTO_AI_PYTHON") ?? "python.exe";
        DateTime started = DateTime.UtcNow;
        try
        {
            int exit = await Task.Run(() =>
            {
                var info = new ProcessStartInfo
                {
                    FileName = python, WorkingDirectory = directory,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                info.ArgumentList.Add("-B");
                info.ArgumentList.Add(script);
                info.ArgumentList.Add("--pipeline-check");
                info.Environment["PYTHONUTF8"] = "1";
                using var child = Process.Start(info);
                if (child is null) return -1;
                if (child.WaitForExit(25000)) return child.ExitCode;
                child.Kill(entireProcessTree: true);
                child.WaitForExit(2000);
                return -2;
            });
            if (IsDisposed) return;
            string path = Path.Combine(Root, "pipeline-check.json");
            using var report = ReadBounded(path);
            if (report is null || File.GetLastWriteTimeUtc(path) < started.AddSeconds(-2) ||
                Field(report.RootElement, "kind") != "local_readiness_only" ||
                !report.RootElement.TryGetProperty("stages", out var stages) ||
                stages.ValueKind != JsonValueKind.Object)
            {
                MessageBox.Show(this, "AI 사전 점검 결과를 읽지 못했습니다. Python 및 작업용 소스 경로를 확인하세요.");
                return;
            }
            string[] keys = { "evidence", "source", "codex", "regressions", "draft_pr" };
            string[] names = { "오류 수집 모듈", "Git 소스 및 깨끗한 상태",
                "Python·Git·Codex CLI", ".NET과 제작·가공 테스트", "GitHub Draft PR (선택)" };
            var lines = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                bool pass = stages.TryGetProperty(keys[i], out var value) &&
                            value.ValueKind == JsonValueKind.True;
                lines.Add((pass ? "준비" : "확인 필요") + " · " + names[i]);
            }
            string prefix = Field(report.RootElement, "result") == "ready_for_manual_trial"
                ? "사전 준비 점검 통과. 실제 Codex 로그인·수정 성공은 아직 검증되지 않았습니다."
                : "실제 Codex 테스트 전 필수 환경 준비가 필요합니다.";
            MessageBox.Show(this, prefix + "\n\n" + string.Join("\n", lines) +
                "\n\n이 검사는 Codex, 게임, 테스트, GitHub PR을 실제 실행하지 않습니다.",
                "AI 자동수정 안전 사전 점검", MessageBoxButtons.OK,
                exit == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!IsDisposed)
                MessageBox.Show(this, "AI 사전 점검 실행 실패 · Python 경로와 설치 상태를 확인하세요.");
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

    private static string PhaseLabel(string stage) => stage switch
    {
        "evidence" => "오류 기록·로그 확인",
        "codex_edit" => "Codex 원인 분석·로직 수정",
        "diff_review" => "변경 파일·안전 조건 확인",
        "regression" => "빌드·회귀 테스트",
        "repair" => "테스트 실패 후 격리 재수정",
        "draft" => "수정안 정리·Draft PR 검토",
        _ => "대기"
    };

    private void Reload()
    {
        if (IsDisposed) return;
        bool running = _isWatcherRunning();
        string active = "", activity = "기록 없음", currentStep = "", stepStarted = "";
        int pending = 0;
        using (var state = ReadBounded(Path.Combine(Root, "watch-activity.json")))
        {
            if (state is not null)
            {
                activity = Field(state.RootElement, "status", 32);
                active = Field(state.RootElement, "active", 100);
                pending = Number(state.RootElement, "pending");
                currentStep = Field(state.RootElement, "phase", 32);
                stepStarted = Field(state.RootElement, "phase_started", 64);
            }
        }
        string[] stages = { "evidence", "codex_edit", "diff_review", "regression", "draft" };
        if (running && activity == "processing" && stages.Contains(currentStep))
        {
            int index = Array.IndexOf(stages, currentStep);
            string flow = string.Join("  →  ", stages.Select((value, n) =>
                (n < index ? "✓ " : n == index ? "▶ " : "○ ") + PhaseLabel(value)));
            string duration = DateTimeOffset.TryParse(stepStarted, out var start)
                ? $" · 이 단계 {(int)Math.Max(0, (DateTimeOffset.Now - start).TotalSeconds)}초 경과"
                : "";
            _pipeline.Text = "현재 단계: " + PhaseLabel(currentStep) + duration +
                "\n" + flow;
        }
        else if (running && activity == "processing" && currentStep == "repair")
        {
            string duration = DateTimeOffset.TryParse(stepStarted, out var start)
                ? $" · {(int)Math.Max(0, (DateTimeOffset.Now - start).TotalSeconds)}초 경과" : "";
            _pipeline.Text = "현재 단계: 테스트 실패 후 격리 재수정" + duration +
                "\n기존 테스트 실패 → Codex 재수정 → 다시 빌드·회귀 테스트";
        }
        else
        {
            _pipeline.Text = "현재 단계: " + (running ? "새 오류 대기 또는 결과 확인" : "AI 감시 꺼짐") +
                "\n실제 Codex 분석과 로직 수정은 한 작업이며, 완료 전에는 성공으로 표시하지 않습니다.";
        }
        string phase = activity switch
        {
            "processing" => "지금 오류를 분석하고 있습니다.",
            "quota_paused" => "Codex 사용량을 소진해 분석을 멈췄습니다.",
            "safe_stop" => "안전 검사 문제로 AI가 중지됐습니다.",
            "watching" => "새 오류가 생기면 분석을 시작합니다.",
            _ => "AI 상태를 확인하는 중입니다."
        };
        _overview.Text = (running ? "● AI 감시 켜짐" : "○ AI 감시 꺼짐") +
            "  ·  " + (running ? phase : "오류는 기록하지만 Codex가 자동 분석하지 않습니다.") +
            "\n대기 중인 오류 " + pending + "건  ·  " +
            (running && active.Length > 0 ? "지금 분석: " + active : "현재 분석 중인 오류 없음") +
            "\n" + _watcherStatus();

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
                    .Select(x => Path.Combine(x, "result.json")).Where(File.Exists)
                    .Select(p => new FileInfo(p)).OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(250).ToArray()
                : Array.Empty<FileInfo>();
            var analyses = new List<Analysis>();
            var byEvent = new Dictionary<string, Analysis>(StringComparer.OrdinalIgnoreCase);
            int success = 0, failed = 0, review = 0, activeCount = 0;
            foreach (var file in files)
            {
                using var report = ReadBounded(file.FullName);
                if (report is null) continue;
                var obj = report.RootElement;
                string status = Field(obj, "status", 64);
                if (status == "tests_passed_review_required") success++;
                else if (status is "tests_failed" or "blocked") failed++;
                else if (status is "needs_review" or "interrupted_review_required") review++;
                else if (status == "in_progress") activeCount++;
                string eventId = Field(obj, "event_id", 100);
                if (eventId.Length == 0) eventId = file.Directory?.Name ?? "";
                string eventName = Field(obj, "event", 140);
                string reason = Field(obj, "reason", 500);
                string url = "", prText = "없음";
                if (obj.TryGetProperty("pr", out var pr) && pr.ValueKind == JsonValueKind.Object)
                {
                    url = Field(pr, "url", 150);
                    string stateName = Field(pr, "status", 40);
                    if (stateName == "draft_created" && IsTrustedPrUrl(url))
                        prText = "PR " + url[(url.LastIndexOf('/') + 1)..];
                    else if (stateName == "blocked") prText = "저장 보류";
                    else if (stateName == "disabled") prText = "저장 안 함";
                }
                string baseCommit = Field(obj, "base_commit", 40);
                string releaseCommit = Field(obj, "release_source_sha", 40);
                string sourceLabel = baseCommit.Length == 40
                    ? baseCommit[..10] : "기록 없음";
                string expectedLabel = releaseCommit.Length == 40
                    ? releaseCommit[..10] : "과거 분석 (불명)";
                string ciLabel = "최종 PR CI는 로컬 기록으로 미검증";
                if (obj.TryGetProperty("pr_ci", out var ci) && ci.ValueKind == JsonValueKind.Object)
                {
                    string ciStatus = Field(ci, "status", 40);
                    if (ciStatus == "not_checked") ciLabel = "PR CI 별도 확인 필요";
                }
                string provenance = " · 로컬 Git " + sourceLabel +
                    " · 배포 기준 " + expectedLabel + " · " + ciLabel;
                string feedback = status == "tests_passed_review_required" && ValidEventId(eventId)
                    ? ManualVerdict(file.DirectoryName ?? "", eventId) : "unverified";
                var analysis = new Analysis(eventName, eventId, file.FullName,
                    TimeOf(Field(obj, "started", 70), file.LastWriteTimeUtc),
                    status, reason, IsTrustedPrUrl(url) ? url : "", prText,
                    feedback, provenance);
                analyses.Add(analysis);
                if (!string.IsNullOrEmpty(eventName) && !byEvent.ContainsKey(eventName))
                    byEvent[eventName] = analysis; // Files are newest first: do not hide a manual reanalysis.
            }

            var items = new List<(DateTimeOffset Time, string Title, string Detail,
                string Status, string Next, string Url, string Pr, string File, string Id)>();
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string localRoot = AiRuntimeErrorJournal.Root;
            if (Directory.Exists(localRoot))
            {
                foreach (string folder in Directory.EnumerateDirectories(localRoot)
                    .OrderByDescending(Directory.GetCreationTimeUtc).Take(250))
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    using var report = ReadBounded(Path.Combine(folder, "summary.json"));
                    if (report is null) continue;
                    var obj = report.RootElement;
                    string name = Path.GetFileName(folder);
                    string title = Field(obj, "title", 120);
                    string detail = Field(obj, "detail", 1500);
                    DateTimeOffset occurred = TimeOf(Field(obj, "timeLocal", 70),
                        Directory.GetCreationTimeUtc(folder));
                    byEvent.TryGetValue(name, out var ai);
                    string aiStatus = ai?.Status ?? "not_started";
                    string status = ai is null ? (running ? "AI 분석 대기" : "AI 분석 대기 · 꺼짐")
                        : (aiStatus == "in_progress" && (!running || active != name)
                            ? "이전 분석 미완료 · 결과 확인 필요" : StatusText(aiStatus));
                    string explanation = ai is { Status: "blocked" or "tests_failed" or "needs_review" or "interrupted_review_required" }
                        ? detail + " / " + ReasonText(aiStatus, ai.Reason) : detail;
                    if (aiStatus == "tests_passed_review_required")
                        explanation += " / 최초 AI 후보만 로컬 테스트됨; PR 최종 커밋 CI·실게임은 별도 검증";
                    if (ai is not null) explanation += ai.Provenance;
                    items.Add((occurred, FriendlyError(title.Length > 0 ? title : "오류", detail),
                        explanation, status, NextStep(aiStatus, ai?.Reason ?? "", running),
                        ai?.Url ?? "", ai?.PrText ?? "없음",
                        ai?.FilePath ?? "", ai?.EventId ?? ""));
                    shown.Add(name);
                }
            }
            foreach (var ai in analyses)
            {
                if (shown.Contains(ai.EventName)) continue;
                string title = ai.EventName.StartsWith("runtime_", StringComparison.OrdinalIgnoreCase)
                    ? "앱 오류 기록" : "외부 MCP 오류 기록";
                items.Add((ai.Time, title,
                    ReasonText(ai.Status, ai.Reason) + ai.Provenance +
                    " · 원본 오류는 PC의 로컬 기록에 보관됨",
                    ai.Status == "in_progress" && (!running || active != ai.EventName)
                        ? "이전 분석 미완료 · 결과 확인 필요" : StatusText(ai.Status),
                    NextStep(ai.Status, ai.Reason, running),
                    ai.Url, ai.PrText, ai.FilePath, ai.EventId));
            }
            DateTimeOffset now = DateTimeOffset.Now;
            int rangeIndex = _range.SelectedIndex;
            var filtered = items
                .Where(x => rangeIndex switch
                {
                    1 => x.Time.ToLocalTime().Date == now.Date,
                    2 => x.Time >= now.AddDays(-7),
                    _ => true
                })
                .OrderByDescending(x => x.Time).Take(100).ToArray();

            _recent.Rows.Clear();
            _details.Text = "오류를 선택하면 발생 내용과 AI 처리 상태를 자세히 볼 수 있습니다.";
            foreach (var item in filtered)
            {
                int row = _recent.Rows.Add(
                    item.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    item.Title, item.Detail, item.Status, item.Next, item.Pr);
                _recent.Rows[row].Tag = new ResultItem(item.Url, item.File,
                    item.Id, item.Title, item.Detail, item.Status, item.Next);
                _recent.Rows[row].Cells[2].ToolTipText = item.Detail;
                if (item.Status.Contains("중단") || item.Status.Contains("실패"))
                    _recent.Rows[row].DefaultCellStyle.ForeColor = UiError;
            }
            _recent.ClearSelection();
            string retentionLabel = "작업공간 상태 미확인";
            using (var inventory = ReadBounded(Path.Combine(Root, "worktree-retention.json")))
            {
                if (inventory is not null && Field(inventory.RootElement, "kind") == "retention_read_only")
                {
                    int count = Number(inventory.RootElement, "count");
                    int limit = Number(inventory.RootElement, "limit");
                    if (limit > 0)
                        retentionLabel = "작업공간 " + count + "/" + limit +
                            (count >= limit ? " · 가득 참" : count >= limit - 3 ? " · 곧 가득 참" : "");
                }
            }
            _counts.Text = $"오류 기록 {items.Count}건 · 초기 후보 테스트 통과 {success}건 · " +
                $"분석 실패/중단 {failed}건 · 검토 필요 {review}건 · 진행 중 {activeCount}건";
            _updated.Text = $"마지막 확인 {DateTime.Now:HH:mm:ss} · 현재 화면 {filtered.Length}건 · " +
                retentionLabel + " · 초기 후보 PASS는 게임 해결이 아닙니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _updated.Text = "AI 분석 결과 폴더를 읽지 못했습니다.";
        }
    }
}
