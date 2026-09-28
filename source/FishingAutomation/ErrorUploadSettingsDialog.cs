namespace FishingAutomation;

internal sealed class ErrorUploadSettingsDialog : Form
{
    private readonly CheckBox _enabled = new();
    private readonly TextBox _repo = new();
    private readonly TextBox _branch = new();
    private readonly TextBox _token = new();
    private readonly CheckBox _screenshot = new();
    private readonly NumericUpDown _logLines = new();
    private readonly Label _result = new();
    private readonly Func<string, string, string?, Task<(bool Ok, string Message)>> _test;

    public bool UploadEnabled => _enabled.Checked;
    public string Repository => _repo.Text.Trim();
    public string Branch => _branch.Text.Trim();
    public string Token => _token.Text.Trim();
    public bool SendScreenshot => _screenshot.Checked;
    public int RecentLogLines => (int)_logLines.Value;

    public ErrorUploadSettingsDialog(
        ErrorUploadSettings current,
        bool hasStoredToken,
        Func<string, string, string?, Task<(bool Ok, string Message)>> test)
    {
        _test = test;
        Text = "실전 에러 자동 전송";
        AccessibleName = "실전 에러 자동 전송 설정";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(610, 465);
        BackColor = Color.FromArgb(3, 28, 49);
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9.5f);
        ShowInTaskbar = false;

        Controls.Add(new Label
        {
            Text = "실전 에러 자동 전송",
            Location = new Point(24, 18), Size = new Size(420, 34),
            Font = new Font("맑은 고딕", 16f, FontStyle.Bold), ForeColor = Color.White
        });
        Controls.Add(new Label
        {
            Text = "오류/자동복구 경고 발생 시 summary.json, 최근 로그, 게임 스크린샷을 비공개 GitHub 저장소로 전송합니다.",
            Location = new Point(26, 55), Size = new Size(555, 42), ForeColor = Color.FromArgb(188, 211, 233)
        });

        _enabled.Text = "에러 자동 전송 사용";
        _enabled.Location = new Point(28, 103); _enabled.Size = new Size(230, 28); _enabled.Checked = current.Enabled;
        Controls.Add(_enabled);

        AddField("저장소 (owner/repo)", _repo, 145);
        _repo.Text = current.Repository;
        AddField("브랜치", _branch, 190);
        _branch.Text = string.IsNullOrWhiteSpace(current.Branch) ? "main" : current.Branch;
        AddField("Fine-grained token", _token, 235);
        _token.UseSystemPasswordChar = true;
        _token.PlaceholderText = hasStoredToken ? "저장된 토큰 유지 (변경할 때만 입력)" : "토큰 입력";

        _screenshot.Text = "게임 화면 스크린샷 포함";
        _screenshot.Location = new Point(176, 282); _screenshot.Size = new Size(230, 28); _screenshot.Checked = current.SendScreenshot;
        Controls.Add(_screenshot);

        Controls.Add(new Label { Text = "최근 로그 줄 수", Location = new Point(30, 322), Size = new Size(140, 28), TextAlign = ContentAlignment.MiddleLeft });
        _logLines.Location = new Point(176, 322); _logLines.Size = new Size(100, 28); _logLines.Minimum = 50; _logLines.Maximum = 1000; _logLines.Increment = 50;
        _logLines.Value = Math.Clamp(current.RecentLogLines, 50, 1000);
        Controls.Add(_logLines);

        var testButton = MakeButton("연결 테스트", new Rectangle(30, 365, 125, 38), Color.FromArgb(8, 66, 105));
        testButton.Click += async (_, _) =>
        {
            testButton.Enabled = false;
            _result.Text = "확인 중...";
            var r = await _test(_repo.Text.Trim(), _branch.Text.Trim(), string.IsNullOrWhiteSpace(_token.Text) ? null : _token.Text.Trim());
            _result.Text = r.Message;
            _result.ForeColor = r.Ok ? Color.FromArgb(80, 230, 155) : Color.Salmon;
            testButton.Enabled = true;
        };
        Controls.Add(testButton);

        _result.Location = new Point(170, 365); _result.Size = new Size(405, 44); _result.TextAlign = ContentAlignment.MiddleLeft; _result.AutoEllipsis = true;
        Controls.Add(_result);

        var save = MakeButton("저장", new Rectangle(383, 415, 92, 36), Color.FromArgb(0, 122, 190));
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("취소", new Rectangle(485, 415, 92, 36), Color.FromArgb(20, 52, 78));
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(save); Controls.Add(cancel);
        AcceptButton = save; CancelButton = cancel;
    }

    private void AddField(string caption, TextBox box, int y)
    {
        Controls.Add(new Label { Text = caption, Location = new Point(30, y), Size = new Size(140, 30), TextAlign = ContentAlignment.MiddleLeft });
        box.Location = new Point(176, y); box.Size = new Size(400, 30); box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(0, 17, 32); box.ForeColor = Color.White;
        Controls.Add(box);
    }

    private static Button MakeButton(string text, Rectangle bounds, Color backColor)
    {
        return new Button
        {
            Text = text, Bounds = bounds, BackColor = backColor, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
    }
}
