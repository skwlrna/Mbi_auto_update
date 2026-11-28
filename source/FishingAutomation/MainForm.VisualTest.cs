namespace FishingAutomation;

public sealed partial class MainForm
{
    private void ShowGameWindowDiagnostic()
    {
        var windows = DungeonVisionBot.WindowTools.EnumerateVisibleWindows();
        nint foreground = DungeonVisionBot.NativeMethods.GetForegroundWindow();
        string foregroundTitle = DungeonVisionBot.WindowTools.GetWindowTitle(foreground);
        if (windows.Count == 0)
        {
            MessageBox.Show(this, "표시 중인 마비노기 모바일 창이 없습니다. 최소화 여부를 확인하세요.",
                "게임 창 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "게임 창 확인", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(620, 430), MinimumSize = new Size(440, 300)
        };
        using var details = new Label { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8) };
        using var choices = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        using var preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };

        void ShowSelection()
        {
            if (choices.SelectedItem is not DungeonVisionBot.WindowItem window) return;
            preview.Image?.Dispose();
            preview.Image = null;
            details.Text = $"선택한 게임 창  {window.Title}\n진단 시작 시 전면 창  {foregroundTitle}";
            if (window.Handle != foreground)
                details.Text += "\n게임 영역을 덮고 있는 창도 미리보기에 나타납니다.";
            try
            {
                preview.Image = new DungeonVisionBot.WindowCapture().CaptureClient(window.Handle);
            }
            catch (Exception ex)
            {
                details.Text += "\n캡처 실패  " + ex.Message;
            }
        }

        choices.Items.AddRange(windows.Cast<object>().ToArray());
        choices.Visible = windows.Count > 1;
        choices.SelectedIndex = Math.Max(0, windows.FindIndex(window => window.Handle == foreground));
        choices.SelectedIndexChanged += (_, _) => ShowSelection();
        ShowSelection();
        dialog.Controls.Add(preview);
        dialog.Controls.Add(choices);
        dialog.Controls.Add(details);
        try { dialog.ShowDialog(this); }
        finally { preview.Image?.Dispose(); }
    }

    internal async void ShowVisualRecognitionTest()
    {
        if (AnyRunning)
        {
            MessageBox.Show(this, "매크로 실행 중에는 인식 테스트를 시작하지 않습니다.\nF10으로 정지한 뒤 실행하세요.",
                "MABI AUTO 인식 테스트", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "저장 스크린샷 인식 테스트",
            AccessibleName = "저장 스크린샷 인식 테스트",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(760, 590),
            BackColor = Color.FromArgb(3, 28, 49),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f),
            ShowInTaskbar = false
        };

        var title = new Label
        {
            Text = "OCR / 이미지 인식 오프라인 테스트",
            Location = new Point(24, 18), Size = new Size(690, 34),
            Font = new Font("맑은 고딕", 16f, FontStyle.Bold)
        };
        var note = new Label
        {
            Text = "실제 게임에는 키보드/마우스를 보내지 않습니다. 이전에 저장한 스크린샷을 현재 OCR/OpenCV 검출기에 넣어 확인합니다.",
            Location = new Point(26, 58), Size = new Size(700, 45),
            ForeColor = Color.FromArgb(188, 211, 233)
        };
        var output = new TextBox
        {
            Location = new Point(24, 112), Size = new Size(712, 390),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(0, 15, 29), ForeColor = Color.White,
            Font = new Font("Consolas", 9.5f), WordWrap = true
        };
        var status = new Label
        {
            Text = "준비",
            Location = new Point(24, 510), Size = new Size(430, 42),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(188, 211, 233)
        };
        var run = new Button
        {
            Text = "전체 테스트 실행",
            Location = new Point(470, 516), Size = new Size(135, 40),
            BackColor = Color.FromArgb(0, 122, 190), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        var close = new Button
        {
            Text = "닫기",
            Location = new Point(615, 516), Size = new Size(120, 40),
            BackColor = Color.FromArgb(20, 52, 78), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        close.Click += (_, _) => dialog.Close();

        dialog.Controls.AddRange(new Control[] { title, note, output, status, run, close });

        run.Click += async (_, _) =>
        {
            run.Enabled = false;
            close.Enabled = false;
            output.Clear();
            status.Text = "테스트 실행 중...";
            status.ForeColor = Color.FromArgb(255, 194, 87);

            try
            {
                var progress = new Progress<string>(line =>
                {
                    output.AppendText(line + Environment.NewLine);
                    output.SelectionStart = output.TextLength;
                    output.ScrollToCaret();
                });

                var tester = new VisualRecognitionTester(AppContext.BaseDirectory);
                VisualTestRunReport report = await tester.RunAllAsync(progress);

                string? uploaded = await _errorUploader.UploadVisualTestRunAsync(report);
                if (!string.IsNullOrWhiteSpace(uploaded))
                    output.AppendText($"[TEST-UPLOAD] GitHub 전송 완료 · {uploaded}{Environment.NewLine}");
                else
                    output.AppendText("[TEST-UPLOAD] 에러 전송 설정 OFF/불완전 · 로컬 결과만 표시합니다." + Environment.NewLine);

                status.Text = report.Failed == 0
                    ? $"완료 · PASS {report.Passed} / FAIL 0 / SKIP {report.Skipped}"
                    : $"완료 · PASS {report.Passed} / FAIL {report.Failed} / SKIP {report.Skipped}";
                status.ForeColor = report.Failed == 0
                    ? Color.FromArgb(80, 230, 155)
                    : Color.Salmon;

                _log.Write($"[인식테스트] 완료 · PASS={report.Passed} FAIL={report.Failed} SKIP={report.Skipped}" +
                           (uploaded is null ? "" : $" · upload={uploaded}"));
            }
            catch (Exception ex)
            {
                output.AppendText("[TEST-ERROR] " + ex + Environment.NewLine);
                status.Text = "테스트 실행 오류";
                status.ForeColor = Color.Salmon;
                _log.Write("[인식테스트] 실행 오류: " + ex.Message);
            }
            finally
            {
                run.Enabled = true;
                close.Enabled = true;
            }
        };

        dialog.ShowDialog(this);
        await Task.CompletedTask;
    }
}
