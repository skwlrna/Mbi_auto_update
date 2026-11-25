// Presentation-only palette shared by production and scheduler pages.
// No game, packet, command, or persisted task data is touched by theme changes.
namespace FishingAutomation;

public sealed partial class MainForm
{
    private interface ITaskThemePage
    {
        TaskPalette Palette { get; }
    }

    private sealed class TaskPalette
    {
        private readonly bool _dark;
        private TaskPalette(bool dark) => _dark = dark;
        internal static readonly TaskPalette Light = new(false);
        internal static readonly TaskPalette Dark = new(true);

        internal Color WindowBg => _dark ? Color.FromArgb(23, 23, 29) : Color.FromArgb(250, 251, 252);
        internal Color PanelBg => _dark ? Color.FromArgb(30, 31, 40) : Color.FromArgb(239, 241, 247);
        internal Color Border => _dark ? Color.FromArgb(60, 63, 79) : Color.FromArgb(214, 219, 230);
        internal Color TitleText => _dark ? Color.FromArgb(239, 241, 250) : Color.FromArgb(35, 39, 56);
        internal Color Blue => _dark ? Color.FromArgb(173, 186, 255) : Color.FromArgb(7, 81, 213);
        internal Color Green => _dark ? Color.FromArgb(107, 222, 164) : Color.FromArgb(22, 128, 81);
        internal Color CardBg => _dark ? Color.FromArgb(35, 37, 46) : Color.FromArgb(255, 255, 255);
        internal Color CardBg2 => _dark ? Color.FromArgb(48, 51, 64) : Color.FromArgb(235, 238, 245);
        internal Color Accent => Blue;
        internal Color AccentSoft => _dark ? Color.FromArgb(48, 57, 87) : Color.FromArgb(233, 237, 255);
        internal Color Muted => _dark ? Color.FromArgb(170, 179, 200) : Color.FromArgb(93, 101, 117);
        internal Color Line => _dark ? Color.FromArgb(62, 66, 82) : Color.FromArgb(218, 222, 230);
        internal Color PrimaryText => _dark ? Color.FromArgb(22, 22, 25) : Color.White;
        internal Color Warning => _dark ? Color.FromArgb(255, 183, 105) : Color.FromArgb(151, 79, 13);
        internal Color Error => _dark ? Color.FromArgb(255, 175, 184) : Color.FromArgb(175, 34, 48);

        internal static TaskPalette For(Control? control)
        {
            for (var current = control; current != null; current = current.Parent)
                if (current is ITaskThemePage page) return page.Palette;
            return Light;
        }

        private Color[] Backgrounds => new[] { WindowBg, PanelBg, CardBg, CardBg2, Accent, AccentSoft, Border, Line };
        private Color[] Foregrounds => new[] { TitleText, Blue, Green, Muted, PrimaryText, Warning, Error };
        internal Color Map(Color value, TaskPalette target, bool foreground)
        {
            if (value.IsEmpty || value.A != 255) return value;
            var source = foreground ? Foregrounds : Backgrounds;
            var destination = foreground ? target.Foregrounds : target.Backgrounds;
            for (int i = 0; i < source.Length; i++)
                if (value.ToArgb() == source[i].ToArgb()) return destination[i];
            return value;
        }
    }

    private static void ApplyTaskPalette(Control root, TaskPalette previous, TaskPalette next)
    {
        // Snapshot before touching parents so inherited colors are not remapped twice.
        var snapshots = new List<(Control Control, Color Back, Color Fore)>();
        void Snapshot(Control control)
        {
            snapshots.Add((control, control.BackColor, control.ForeColor));
            foreach (Control child in control.Controls) Snapshot(child);
        }
        Snapshot(root);
        root.SuspendLayout();
        try
        {
            foreach (var (control, back, fore) in snapshots)
            {
                var nextBack = previous.Map(back, next, false);
                var nextFore = previous.Map(fore, next, true);
                if (control.BackColor.ToArgb() != nextBack.ToArgb()) control.BackColor = nextBack;
                if (control.ForeColor.ToArgb() != nextFore.ToArgb()) control.ForeColor = nextFore;
                if (control is Button button && button.FlatStyle == FlatStyle.Flat)
                {
                    var style = button.FlatAppearance;
                    style.BorderColor = previous.Map(style.BorderColor, next, false);
                    style.MouseOverBackColor = previous.Map(style.MouseOverBackColor, next, false);
                    style.MouseDownBackColor = previous.Map(style.MouseDownBackColor, next, false);
                }
                control.Invalidate();
            }
        }
        finally { root.ResumeLayout(true); }
        root.Invalidate(true);
    }
}
