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
                MessageBox.Show(this,
                    "이어할 최근 대량가공 작업이 없습니다. 새 작업은 F9로 시작하세요.",
                    "대량가공 이어하기", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    if (MessageBox.Show(this,
                            "최근 대량가공의 이어하기 기록을 초기화하시겠습니까?" +
                            Environment.NewLine + Environment.NewLine +
                            "게임 내 작업·완성품은 그대로이며, 원본 배치 기록도 삭제하지 않습니다." +
                            Environment.NewLine +
                            "초기화한 기록은 자동 이어하기 목록에 다시 나타나지 않습니다.",
                            "대량가공 기록 초기화",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    using (var lease = new MultiAlteringBatchStore(root))
                        MultiAlteringRecentResume.HideLatest(root, recent.BatchId);
                    _log.Write("[다중가공] 최근 이어하기 대상 초기화 · " +
                        recent.BatchId + " · 원본/게임 작업 보존");
                    MessageBox.Show(this, "최근 이어하기 대상이 초기화됐습니다." +
                        Environment.NewLine + "기존 게임 작업과 생산품은 변경되지 않았습니다.",
                        "초기화 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            // Never change a manifest or send a game input while verifying.
            await CliAutomationGuards.EnsureCapabilitiesAsync(_cli,
                new[] { "get_my_info", "get_altering_works", "get_items", "get_inventory" },
                CancellationToken.None);
            var identity = await CliIdentityGuard.CaptureForMultiAlteringAsync(
                _cli, CancellationToken.None, allowLimitedFreshTest: true);
            var data = new AlteringCliData(_cli);
            IReadOnlyList<MultiAlteringResumeLine> lines;
            using (var lease = new MultiAlteringBatchStore(root))
            {
                if (MultiAlteringRecentResume.Latest(root)?.BatchId != recent.BatchId)
                    throw new InvalidOperationException("최근 이어하기 대상이 변경됐습니다.");
                lines = await MultiAlteringRecentResume.VerifyAsync(
                    recent, identity.Baseline, data, CancellationToken.None);
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
            await StartMultiAlteringAsync(recent.Plans, recent.Directory, recent.BatchId);
        }
        catch (Exception ex)
        {
            _log.Write("[다중가공] 이어하기 검증/초기화 실패 · 입력 없음 · " + ex.Message);
            MessageBox.Show(this, ex.Message, "대량가공 이어하기 차단",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _resumeUiBusy = false;
            RefreshProductionDashboard();
        }
    }

    private static Form MakeResumeDialog(string title, string headline, string details,
        params (string Label, DialogResult Result)[] commands)
    {
        var dialog = new Form
        {
            Text = title,
            Width = 620,
            Height = 390,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = WindowBg,
            ForeColor = TitleText,
            Font = new Font("맑은 고딕", 10f),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Padding = new Padding(18, 16, 18, 14),
            BackColor = WindowBg
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, Text = headline, ForeColor = TitleText,
            Font = new Font("맑은 고딕", 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, TabStop = false,
            Text = details, BackColor = PanelBg, ForeColor = TitleText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 10f)
        }, 0, 1);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        foreach (var command in commands)
        {
            var btn = new Button
            {
                Text = command.Label, DialogResult = command.Result,
                Width = command.Label.Length >= 7 ? 150 : 112, Height = 39,
                Margin = new Padding(7, 4, 0, 4),
                BackColor = command.Result == DialogResult.OK ? Blue : PanelBg,
                ForeColor = command.Result == DialogResult.OK ? Color.White : TitleText,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 10f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderSize = 0;
            buttons.Controls.Add(btn);
            if (command.Result == DialogResult.OK) dialog.AcceptButton = btn;
            if (command.Result == DialogResult.Cancel) dialog.CancelButton = btn;
        }
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        return dialog;
    }
}
