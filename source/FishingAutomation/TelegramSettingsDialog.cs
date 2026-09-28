using System.Text.Json;

namespace FishingAutomation;

internal sealed class TelegramSettingsDialog : Form
{
    private readonly TextBox _token = new();
    private readonly TextBox _chatId = new();

    public string BotToken => _token.Text.Trim();
    public string ChatId => _chatId.Text.Trim();

    public TelegramSettingsDialog(NotificationSettings current)
    {
        Text = "텔레그램 설정";
        AccessibleName = "텔레그램 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 300);
        BackColor = Color.FromArgb(2, 18, 34);
        ForeColor = Color.FromArgb(225, 238, 252);
        Font = new Font("맑은 고딕", 10f);
        ShowInTaskbar = false;

        var border = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Color.FromArgb(0, 190, 255) };
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(3, 28, 49) };
        border.Controls.Add(body);
        Controls.Add(border);

        var title = new Label
        {
            Text = "텔레그램 설정", Location = new Point(24, 17), Size = new Size(370, 38),
            Font = new Font("맑은 고딕", 17f, FontStyle.Bold), ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var close = CreateButton("×", new Rectangle(426, 12, 38, 38), Color.FromArgb(8, 39, 65));
        close.Font = new Font("Segoe UI", 18f, FontStyle.Regular);
        close.DialogResult = DialogResult.Cancel;

        body.Controls.Add(title);
        body.Controls.Add(close);
        AddField(body, "봇 토큰", _token, 76);
        AddField(body, "채팅 ID", _chatId, 143);
        _token.Text = current.BotToken;
        _chatId.Text = current.ChatId;
        _token.UseSystemPasswordChar = true;

        var save = CreateButton("저장", new Rectangle(246, 231, 98, 45), Color.FromArgb(0, 122, 190));
        save.DialogResult = DialogResult.OK;
        var cancel = CreateButton("취소", new Rectangle(354, 231, 98, 45), Color.FromArgb(8, 39, 65));
        cancel.DialogResult = DialogResult.Cancel;
        body.Controls.Add(save);
        body.Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddField(Control parent, string caption, TextBox box, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = caption, Location = new Point(26, y), Size = new Size(100, 32),
            ForeColor = Color.FromArgb(205, 222, 240), TextAlign = ContentAlignment.MiddleLeft
        });
        box.Location = new Point(126, y);
        box.Size = new Size(326, 32);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(0, 17, 32);
        box.ForeColor = Color.White;
        parent.Controls.Add(box);
    }

    private static Button CreateButton(string text, Rectangle bounds, Color backColor)
    {
        return new Button
        {
            Text = text, Bounds = bounds, BackColor = backColor, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
        };
    }
}

public sealed partial class MainForm
{
    private async void ShowTelegramSettings()
    {
        using var dialog = new TelegramSettingsDialog(_notifier.Settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var settings = _notifier.Settings;
        settings.BotToken = dialog.BotToken;
        settings.ChatId = dialog.ChatId;
        settings.Enabled = settings.BotToken.Length > 0 && settings.ChatId.Length > 0;

        string runtimePath = Path.Combine(AppContext.BaseDirectory, "notification.json");
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(runtimePath, json);

        string packageCopy = Path.Combine(UpdateManager.FindPackageRoot(), "FishingAutomation", "notification.json");
        if (!Path.GetFullPath(packageCopy).Equals(Path.GetFullPath(runtimePath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(packageCopy)!);
            File.WriteAllText(packageCopy, json);
        }

        _log.Write(settings.Enabled ? "[텔레그램] 설정 저장 완료" : "[텔레그램] 설정 해제 완료");
        await RunStartupDiagnosticsAsync();
        UpdateDashboard();
    }
}
