namespace FishingAutomation;

public sealed partial class MainForm
{
    private void ShowAbyssRoundHistory()
    {
        string[] rounds = AbyssRoundHistory.Latest(50);
        using var dialog = new Form
        {
            Text = "어비스 판별 기록",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 490),
            MinimizeBox = false
        };
        dialog.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = rounds.Length == 0 ? "기록이 없습니다." : string.Join("\r\n\r\n", rounds)
        });
        dialog.ShowDialog(this);
    }
}
