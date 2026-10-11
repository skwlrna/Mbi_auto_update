using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    // The current production page is unchanged except for one footer button.
    // These are modal, read-only stages until the final user confirmation.
    private async Task ShowRecentMultiAlteringAsync()
    {
        if (AnyRunning) return;
        _resumeUiBusy = true; // F9/hotkeys cannot start a second operation.
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MabiAuto", "multi-altering");

            MultiAlteringRecentRun? recent;
            using (var lease = new MultiAlteringBatchStore(root))
                recent = MultiAlteringRecentResume.Latest(root);

            if (recent is null)
            {
                ShowResumeNotice("대량가공 이어하기", "이어할 최근 작업 없음",
                    "이어할 최근 대량가공 작업이 없습니다." + Environment.NewLine +
                    "새 작업은 기존처럼 F9로 시작할 수 있습니다.");
                return;
            }

            using (var first = MakeResumeDialog("대량가공 이어하기",
                "최근 대량가공 작업",
                "작업 ID: " + recent.BatchId[..8] + Environment.NewLine +
                "시작 시각: " + recent.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine,
                    recent.Plans.Select(p => p.DisplayName + " · 목표 " +
                        p.TargetQuantity.ToString("N0") + "개")) +
                Environment.NewLine + Environment.NewLine +
                "게임에 입력하지 않고 현재 대기열·완성품부터 검증합니다.",
                ("현재 상태 검증", DialogResult.OK),
                ("초기화", DialogResult.Abort),
                ("취소", DialogResult.Cancel)))
            {
                var choice = first.ShowDialog(this);
                if (choice == DialogResult.Cancel || choice == DialogResult.None) return;
                if (choice == DialogResult.Abort)
                {
                    using (var reset = MakeResumeDialog(
                        "대량가공 기록 초기화", "최근 작업 초기화",
                        "최근 대량가공의 이어하기 대상을 초기화하시겠습니까?" +
                        Environment.NewLine + Environment.NewLine +
                        "게임에 등록된 작업·완성품은 그대로 유지합니다." +
                        Environment.NewLine +
                        "원본 배치 기록도 삭제하지 않습니다." +
                        Environment.NewLine +
                        "현재 작업은 이어하기 목록에서만 숨깁니다.",
                        ("초기화", DialogResult.Yes),
                        ("취소", DialogResult.Cancel)))
                        if (reset.ShowDialog(this) != DialogResult.Yes) return;

                    using (var lease = new MultiAlteringBatchStore(root))
                        MultiAlteringRecentResume.HideLatest(root, recent.BatchId);
                    _log.Write("[다중가공] 최근 이어하기 대상 초기화 · " +
                        recent.BatchId + " · 원본/게임 작업 보존");
                    ShowResumeNotice("초기화 완료", "최근 작업 기록 숨김",
                        "최근 이어하기 대상이 초기화됐습니다." + Environment.NewLine +
                        "게임 작업·완성품과 원본 기록은 변경하지 않았습니다.");
                    return;
                }
            }

            // Never change a manifest or send a game input while verifying.
            await CliAutomationGuards.EnsureCapabilitiesAsync(_cli,
                new[] { "get_my_info", "get_altering_works", "get_items", "get_inventory" },
                CancellationToken.None);
            var identity = await CliIdentityGuard.CaptureForMultiAlteringAsync(
                _cli, CancellationToken.None, allowLimitedFreshTest: true);
            bool confirmedSingleCharacter = false;
            if (!identity.Baseline.HasDurableMultiIdentity)
            {
                if (string.IsNullOrWhiteSpace(identity.Baseline.RealmName))
                    throw new InvalidOperationException("1캐릭터 이어하기 서버 확인 실패 · 기록 보존");
                using (var consent = MakeResumeDialog(
                    "1캐릭터 이어하기 확인", "같은 캐릭터 사용 확인",
                    "현재 서버: " + identity.Baseline.RealmName + Environment.NewLine +
                    "이 서버의 같은 캐릭터 하나로만 대량가공하셨습니까?" +
                    Environment.NewLine + Environment.NewLine +
                    "서버명만으로 다른 캐릭터를 구별할 수 없습니다." +
                    Environment.NewLine +
                    "다른 캐릭터였다면 반드시 [아니요]를 선택하세요." +
                    Environment.NewLine +
                    "게임 대기열과 수량 검증은 그대로 유지합니다.",
                    ("예, 같은 캐릭터", DialogResult.Yes),
                    ("아니요", DialogResult.Cancel)))
                    if (consent.ShowDialog(this) != DialogResult.Yes) return;
                confirmedSingleCharacter = true;
                _log.Write("[다중가공] 서버명 전용 이어하기 · 사용자가 동일 캐릭터 사용 확인");
            }
            var data = new AlteringCliData(_cli);
            IReadOnlyList<MultiAlteringResumeLine> lines;
            using (var lease = new MultiAlteringBatchStore(root))
            {
                if (MultiAlteringRecentResume.Latest(root)?.BatchId != recent.BatchId)
                    throw new InvalidOperationException("최근 이어하기 대상이 변경됐습니다.");
                lines = await MultiAlteringRecentResume.VerifyAsync(
                    recent, identity.Baseline, data, CancellationToken.None,
                    allowSingleCharacter: confirmedSingleCharacter);
            }

            string report = string.Join(Environment.NewLine, lines.Select(line =>
                line.Plan.DisplayName + " · 목표 " + line.Plan.TargetQuantity.ToString("N0") +
                "개 / 완성 확정 " + line.Confirmed.ToString("N0") +
                "개 / 등록 중 " + line.RegisteredOutput.ToString("N0") +
                "개 상당 / 신규 필요 " + line.AdditionalOutput.ToString("N0") + "개 상당"));
            using (var confirm = MakeResumeDialog("이어하기 검증 결과",
                "현재 상태 검증 통과",
                report + Environment.NewLine + Environment.NewLine +
                "실행 직전에 저장 기록·캐릭터·게임 대기열을 다시 검증합니다." +
                Environment.NewLine + "판단이 불명확하면 등록·수령 없이 정지합니다.",
                ("이어하기 시작", DialogResult.OK),
                ("취소", DialogResult.Cancel)))
            {
                if (confirm.ShowDialog(this) != DialogResult.OK) return;
            }

            _log.Write("[다중가공] 이어하기 수동 확인 · 최근 배치 " + recent.BatchId);
            _resumeUiBusy = false; // StartMultiAlteringAsync sets the running guard synchronously.
            await StartMultiAlteringAsync(
                recent.Plans, recent.Directory, recent.BatchId, confirmedSingleCharacter);
        }
        catch (Exception ex)
        {
            _log.Write("[다중가공] 이어하기 검증/초기화 실패 · 입력 없음 · " + ex.Message);
            ShowResumeNotice("대량가공 이어하기 차단", "상태 검증 실패",
                ex.Message + Environment.NewLine + Environment.NewLine +
                "작업과 저장 기록은 변경하지 않았습니다.");
        }
        finally
        {
            _resumeUiBusy = false;
            RefreshProductionDashboard();
        }
    }

    private void ShowResumeNotice(string title, string headline, string details)
    {
        using var dialog = MakeResumeDialog(
            title, headline, details, ("확인", DialogResult.Cancel));
        dialog.ShowDialog(this);
    }

    // Match the existing production page: borderless navy window, Segoe-free
    // Malgun Gothic pixel fonts, blue action, muted border and inline header.
    // No Windows white title bar, default MessageBox or separate typography.
    private static Form MakeResumeDialog(string title, string headline, string details,
        params (string Label, DialogResult Result)[] commands)
    {
        static Font UiFont(float pixels, FontStyle style = FontStyle.Regular)
            => new("맑은 고딕", pixels, style, GraphicsUnit.Pixel);

        var dialog = new Form
        {
            Text = title,
            Width = 700,
            Height = 430,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Border,
            ForeColor = TitleText,
            Font = UiFont(18),
            AutoScaleMode = AutoScaleMode.None,
            Padding = new Padding(2),
            KeyPreview = true
        };

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBg,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));

        var titleBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
            Margin = Padding.Empty
        };
        titleBar.Controls.Add(new Label
        {
            Text = "Mabi_Auto   /   " + title,
            Font = UiFont(19, FontStyle.Bold),
            ForeColor = TitleText,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 0, 0)
        });
        var close = new Button
        {
            Text = "×",
            DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Right,
            Width = 50,
            BackColor = PanelBg,
            ForeColor = TitleText,
            Font = UiFont(24),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        close.FlatAppearance.BorderSize = 0;
        titleBar.Controls.Add(close);
        close.BringToFront();
        shell.Controls.Add(titleBar, 0, 0);

        shell.Controls.Add(new Label
        {
            Text = headline,
            ForeColor = TitleText,
            Font = UiFont(21, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Margin = new Padding(20, 9, 20, 4),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        var detailsSurface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
            Margin = new Padding(20, 0, 20, 3),
            Padding = new Padding(12)
        };
        var content = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            TabStop = false,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = PanelBg,
            ForeColor = TitleText,
            Font = UiFont(17),
            Text = details,
            WordWrap = true
        };
        detailsSurface.Controls.Add(content);
        shell.Controls.Add(detailsSurface, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = WindowBg,
            Margin = Padding.Empty,
            Padding = new Padding(16, 6, 20, 8)
        };
        Button? safeDefault = null;
        foreach (var command in commands)
        {
            var button = new Button
            {
                Text = command.Label,
                DialogResult = command.Result,
                Width = command.Label.Length > 6 ? 162 : 120,
                Height = 46,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = command.Result is DialogResult.OK or DialogResult.Yes
                    ? Blue : PanelBg,
                ForeColor = TitleText,
                FlatStyle = FlatStyle.Flat,
                Font = UiFont(17, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = command.Result is DialogResult.OK or DialogResult.Yes
                ? Color.FromArgb(0, 207, 255) : Border;
            footer.Controls.Add(button);
            if (command.Result == DialogResult.OK) dialog.AcceptButton = button;
            if (command.Result == DialogResult.Cancel)
            {
                dialog.CancelButton = button;
                safeDefault = button;
            }
        }

        shell.Controls.Add(footer, 0, 3);
        dialog.Controls.Add(shell);
        dialog.Shown += (_, _) => safeDefault?.Focus();
        dialog.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
                e.Handled = true;
            }
        };
        return dialog;
    }
}
