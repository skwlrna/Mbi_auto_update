namespace FishingAutomation;

public sealed partial class MainForm
{
    private void ShowAbyssRoundHistory() => ShowWorkHistoryDialog("어비스 판별 기록", AbyssRoundHistory.Latest(50));

    private void ShowWorkHistoryDialog(string title, string[] rounds)
    {
        using var dialog = new Form
        {
            Text = title,
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
