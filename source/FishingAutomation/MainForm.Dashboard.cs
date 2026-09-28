using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private readonly List<Button> _tabs = new();
    private readonly List<Button> _dungeonButtons = new();
    private readonly List<Panel> _dungeonCards = new();
    private readonly Label _stageTime = new();
    private readonly Label _timeoutValue = new();
    private readonly Label _miniStatus = new();
    private readonly Label _miniInfo = new();
    private readonly Label _currentDungeonValue = new();
    private readonly Label _readyBadge = new();
    private readonly Label _failureValue = new();
    private readonly Label _averageValue = new();
    private readonly Label _startTimeValue = new();
    private readonly Label _sysGameValue = new();
    private readonly Label _sysTemplateValue = new();
    private readonly Label _sysOcrValue = new();
    private readonly Label _sysInputValue = new();
    private readonly Label _sysWatchdogValue = new();
    private readonly Label _sysTelegramValue = new();
    private readonly Label _sysUpdateValue = new();
    private readonly Label _telegramQuickValue = new();
    private readonly Label _updateStatusValue = new();

    private readonly Label _topGameChip = new();
    private readonly Label _topOcrChip = new();
    private readonly Label _topTemplateChip = new();
    private readonly Label _topInputChip = new();
    private readonly Label _currentModeValue = new();
    private readonly Label _currentStepValue = new();
    private readonly Label _recentDetectValue = new();
    private readonly Label _progressLogPreview = new();
    private readonly ProgressBar _progress = new();

    private Panel _fullView = null!;
    private Panel _miniView = null!;
    private Control _dungeonPicker = null!;
    private Control _dungeonGallery = null!;
    private Control _logView = null!;
    private TableLayoutPanel _dashboard = null!;
    private Panel _bottomHost = null!;
    private Button _logToggle = null!;
    private Button _updateButton = null!;
    private Button _mainActionButton = null!;
    private bool _mini, _logExpanded;
    private Size _fullSize;
    private DateTime? _stageStartedAt, _restartAt, _dungeonStoppedAt, _fishingStoppedAt;
    private int _stageLimit = 600, _timeoutExits, _stageProgress;
    private string? _runError;
    private string _recentDetectText = "대기 중";

    private static readonly Color NavBg = Color.FromArgb(5, 18, 33);
    private static readonly Color CardBg = Color.FromArgb(10, 29, 50);
    private static readonly Color CardBg2 = Color.FromArgb(8, 25, 43);
    private static readonly Color Accent = Color.FromArgb(26, 139, 255);
    private static readonly Color AccentSoft = Color.FromArgb(14, 72, 126);
    private static readonly Color Muted = Color.FromArgb(143, 171, 201);
    private static readonly Color Line = Color.FromArgb(23, 58, 91);

    private Button SmallButton(string caption, Action action)
    {
        var b = new Button
        {
            Text = caption,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBg2,
            ForeColor = TitleText,
            Margin = new Padding(3),
            Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold)
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = AccentSoft;
        b.Click += (_, _) => action();
        return b;
    }

    private Button NavButton(string caption, Action action, bool selected = false)
    {
        var b = new Button
        {
            Text = caption,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = selected ? Color.FromArgb(15, 81, 157) : NavBg,
            ForeColor = selected ? Color.White : Color.FromArgb(187, 209, 232),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 0, 0),
            Margin = new Padding(8, 3, 8, 3),
            Font = new Font("맑은 고딕", 10f, selected ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = selected ? 1 : 0;
        b.FlatAppearance.BorderColor = selected ? Accent : NavBg;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(11, 48, 82);
        b.Click += (_, _) => action();
        return b;
    }

    private Label SectionTitle(string text, float size = 10f) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = TitleText,
        Font = new Font("맑은 고딕", size, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private Panel BorderedPanel(Color? bg = null)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = bg ?? CardBg };
        p.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, Line, ButtonBorderStyle.Solid);
        return p;
    }

    private void BuildLegacyLayout()
    {
        _mode.Items.AddRange(new object[] { "낚시", "던전", "어비스" });
        _mode.SelectedIndex = 0;
        _abyssDungeon.Items.AddRange(new object[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" });
        _abyssDungeon.SelectedIndex = 0;

        _fullView = new Panel { Dock = DockStyle.Fill, BackColor = WindowBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMainBody(), 0, 1);
        _fullView.Controls.Add(root);
        Controls.Add(_fullView);

        _miniView = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(14), BackColor = NavBg };
        var mini = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        mini.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        mini.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        mini.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        foreach (var label in new[] { _miniStatus, _miniInfo })
        {
            label.Dock = DockStyle.Fill;
            label.ForeColor = TitleText;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            mini.Controls.Add(label);
        }
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var stop = SmallButton("정지 (F10)", StopSelected); stop.BackColor = Color.FromArgb(126, 39, 55);
        actions.Controls.Add(stop, 0, 0);
        actions.Controls.Add(SmallButton("전체 화면", () => ToggleMini(false)), 1, 0);
        mini.Controls.Add(actions, 0, 2);
        _miniView.Controls.Add(mini);
        Controls.Add(_miniView);
        ApplyLogVisibility();
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = NavBg, Padding = new Padding(22, 4, 18, 4) };
        header.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Line), 0, header.Height - 1, header.Width, header.Height - 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        brand.Controls.Add(new Label { Text = "M  Mabi_Auto", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 17f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        brand.Controls.Add(new Label { Text = "v65.0.0", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(new Label { Text = "●  시스템 상태", Dock = DockStyle.Fill, ForeColor = Green, Font = new Font("맑은 고딕", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight }, 1, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Control StatusChip(Label label, string caption)
    {
        var p = BorderedPanel(CardBg2);
        p.Margin = new Padding(7, 2, 7, 2);
        label.Text = "●  " + caption;
        label.Dock = DockStyle.Fill;
        label.ForeColor = Green;
        label.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        label.TextAlign = ContentAlignment.MiddleCenter;
        p.Controls.Add(label);
        return p;
    }

    private Control BuildMainBody()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildSidebar(), 0, 0);
        body.Controls.Add(BuildContentArea(), 1, 0);
        return body;
    }

    private Control BuildSidebar()
    {
        var side = new Panel { Dock = DockStyle.Fill, BackColor = NavBg, Padding = new Padding(6, 14, 6, 12) };
        var menu = new TableLayoutPanel { Dock = DockStyle.Top, Height = 330, ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) menu.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666f));
        menu.Controls.Add(NavButton("⌂   홈", () => { }, true), 0, 0);
        menu.Controls.Add(NavButton("●   낚시", () => { if (!AnyRunning && _activeMode is null) _mode.SelectedIndex = 0; }), 0, 1);
        menu.Controls.Add(NavButton("⚔   던전", () => { if (!AnyRunning && _activeMode is null) _mode.SelectedIndex = 1; }), 0, 2);
        menu.Controls.Add(NavButton("◇   어비스", () => { if (!AnyRunning && _activeMode is null) _mode.SelectedIndex = 2; }), 0, 3);
        var life = NavButton("◒   생활", () => { }); life.ForeColor = Color.FromArgb(112, 136, 160);
        menu.Controls.Add(life, 0, 4);
        menu.Controls.Add(NavButton("⚙   설정", () => OpenPackagePath(Path.Combine("FishingAutomation", "config.json"))), 0, 5);

        var slogan = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 105,
            Text = "반복은 Mabi_Auto에게,\r\n당신은 더 즐거운 시간에.\r\n\r\nAUTOMATE\r\nYOUR PLAYTIME",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var mini = new Button { Text = "미니 모드", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = NavBg, ForeColor = Muted, Font = new Font("맑은 고딕", 8f), Cursor = Cursors.Hand };
        mini.FlatAppearance.BorderColor = Line;
        mini.Click += (_, _) => ToggleMini(true);
        side.Controls.Add(slogan);
        side.Controls.Add(mini);
        side.Controls.Add(menu);
        return side;
    }

    private Control BuildContentArea()
    {
        var content = new Panel { Dock = DockStyle.Fill, BackColor = WindowBg, Padding = new Padding(10) };
        _dashboard = BuildDashboardGrid();
        _bottomHost = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = WindowBg, Padding = new Padding(0, 4, 0, 0) };
        _logView = BuildLogPanel();
        _logView.Dock = DockStyle.Fill;
        _logView.Visible = false;
        var bar = BuildBottomBar();
        bar.Dock = DockStyle.Top;
        bar.Height = 54;
        _bottomHost.Controls.Add(_logView);
        _bottomHost.Controls.Add(bar);
        bar.BringToFront();
        content.Controls.Add(_dashboard);
        content.Controls.Add(_bottomHost);
        return content;
    }

    private TableLayoutPanel BuildDashboardGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = Padding.Empty, Margin = Padding.Empty, BackColor = WindowBg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 51));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 49));

        var banner = BuildBanner();
        grid.Controls.Add(banner, 0, 0);
        grid.SetColumnSpan(banner, 2);
        var info = BuildInfoRow();
        grid.Controls.Add(info, 0, 1);
        grid.SetColumnSpan(info, 2);
        grid.Controls.Add(BuildProgressAndStats(), 0, 2);
        grid.Controls.Add(BuildSystemStatusCard(), 1, 2);
        var gallery = BuildAbyssDungeonGallery();
        grid.Controls.Add(gallery, 0, 3);
        grid.SetColumnSpan(gallery, 2);
        return grid;
    }

    private Control BuildBanner()
    {
        var card = BorderedPanel(Color.FromArgb(6, 36, 70));
        card.Paint += (_, e) =>
        {
            var r = card.ClientRectangle;
            if (r.Width < 2 || r.Height < 2) return;
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(5, 24, 52), Color.FromArgb(12, 74, 132), 0f);
            e.Graphics.FillRectangle(bg, r);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(100, 71, 176, 255), 2f);
            using var dark = new SolidBrush(Color.FromArgb(150, 2, 17, 35));
            int ground = r.Height - 15;
            int start = Math.Max(430, r.Width / 3);
            for (int i = 0; i < 7; i++)
            {
                int x = start + i * 85;
                int h = 38 + (i % 3) * 12;
                e.Graphics.FillRectangle(dark, x, ground - h, 20, h);
            }
            for (int i = 0; i < 4; i++)
            {
                int x = start + 35 + i * 135;
                e.Graphics.DrawArc(pen, x, ground - 60, 62, 60, 180, 180);
                e.Graphics.DrawLine(pen, x, ground - 30, x, ground);
                e.Graphics.DrawLine(pen, x + 62, ground - 30, x + 62, ground);
            }
            using var moon = new SolidBrush(Color.FromArgb(205, 204, 230, 255));
            e.Graphics.FillEllipse(moon, r.Width - 145, 18, 42, 42);
            e.Graphics.DrawLine(pen, start - 20, ground, r.Width - 20, ground);
        };
        card.Margin = new Padding(4, 2, 4, 7);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(18, 8, 8, 8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));

        var text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        _readyBadge.Text = "●  매크로 준비됨";
        _readyBadge.Dock = DockStyle.Fill;
        _readyBadge.TextAlign = ContentAlignment.BottomLeft;
        _readyBadge.ForeColor = Color.White;
        _readyBadge.Font = new Font("맑은 고딕", 19f, FontStyle.Bold);
        _readyBadge.Padding = new Padding(8, 0, 0, 0);
        var sub = new Label { Text = "모든 시스템 상태를 확인하고 자동 진행을 준비합니다.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(178, 204, 231), Font = new Font("맑은 고딕", 10f), TextAlign = ContentAlignment.TopLeft, Padding = new Padding(10, 5, 0, 0) };
        text.Controls.Add(_readyBadge, 0, 0);
        text.Controls.Add(sub, 0, 1);
        layout.Controls.Add(text, 0, 0);

        var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(7, 27, 48), Margin = new Padding(8, 2, 2, 2) };
        try
        {
            string path = Path.Combine(UpdateManager.FindPackageRoot(), "FishingAutomation", "abyss", "templates", "hallucination_anchorage.png");
            if (File.Exists(path)) { using var img = Image.FromFile(path); picture.Image = new Bitmap(img); }
        }
        catch { }
        layout.Controls.Add(picture, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildInfoRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        row.Controls.Add(InfoCard("◎", "현재 모드", _currentModeValue), 0, 0);
        row.Controls.Add(InfoCard("▣", "선택 던전", _currentDungeonValue), 1, 0);
        row.Controls.Add(InfoCard("☷", "현재 단계", _currentStepValue), 2, 0);
        row.Controls.Add(InfoCard("▧", "최근 인식", _recentDetectValue), 3, 0);
        return row;
    }

    private Control InfoCard(string icon, string caption, Label value)
    {
        var card = BorderedPanel();
        card.Margin = new Padding(4, 2, 4, 6);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12, 8, 10, 8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        layout.Controls.Add(new Label { Text = icon, Dock = DockStyle.Fill, ForeColor = Accent, Font = new Font("Segoe UI Symbol", 18f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        layout.SetRowSpan(layout.GetControlFromPosition(0, 0)!, 2);
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("맑은 고딕", 8.3f), TextAlign = ContentAlignment.BottomLeft }, 1, 0);
        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.ForeColor = Color.White;
        value.Font = new Font("맑은 고딕", 10.4f, FontStyle.Bold);
        value.TextAlign = ContentAlignment.TopLeft;
        value.AutoEllipsis = true;
        layout.Controls.Add(value, 1, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildProgressAndStats()
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        host.Controls.Add(BuildProgressCard(), 0, 0);
        host.Controls.Add(BuildCompactStats(), 1, 0);
        return host;
    }

    private Control BuildProgressCard()
    {
        var card = BorderedPanel();
        card.Margin = new Padding(4, 2, 5, 7);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16, 10, 16, 12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(SectionTitle("▶  자동 진행 상태", 11f), 0, 0);
        _statusValue.Dock = DockStyle.Fill;
        _statusValue.ForeColor = Green;
        _statusValue.Font = new Font("맑은 고딕", 12f, FontStyle.Bold);
        _statusValue.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_statusValue, 0, 1);
        _progress.Dock = DockStyle.Fill;
        _progress.Minimum = 0; _progress.Maximum = 100; _progress.Value = 0;
        layout.Controls.Add(_progress, 0, 2);
        _stageTime.Dock = DockStyle.Fill;
        _stageTime.ForeColor = Muted;
        _stageTime.Font = new Font("맑은 고딕", 8f);
        _stageTime.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(_stageTime, 0, 3);
        _progressLogPreview.Text = "최근 진행 로그가 여기에 표시됩니다.";
        _progressLogPreview.Dock = DockStyle.Fill;
        _progressLogPreview.ForeColor = Color.FromArgb(181, 203, 225);
        _progressLogPreview.BackColor = Color.FromArgb(6, 21, 37);
        _progressLogPreview.Font = new Font("Consolas", 8.3f);
        _progressLogPreview.TextAlign = ContentAlignment.MiddleLeft;
        _progressLogPreview.Padding = new Padding(12, 8, 12, 8);
        _progressLogPreview.AutoEllipsis = true;
        layout.Controls.Add(_progressLogPreview, 0, 4);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildCompactStats()
    {
        var card = BorderedPanel();
        card.Margin = new Padding(5, 2, 4, 7);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 10, 14, 10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        for (int i = 1; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.Controls.Add(SectionTitle("▥  실행 통계", 10.5f), 0, 0);
        layout.Controls.Add(StatRow("성공", _successValue), 0, 1);
        layout.Controls.Add(StatRow("실패", _failureValue), 0, 2);
        layout.Controls.Add(StatRow("복구", _timeoutValue), 0, 3);
        layout.Controls.Add(StatRow("총 실행 시간", _elapsedValue), 0, 4);
        card.Controls.Add(layout);
        return card;
    }

    private Control StatRow(string caption, Label value)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("맑은 고딕", 8.5f), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.ForeColor = Color.White;
        value.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        value.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private Control BuildSystemStatusCard()
    {
        var card = BorderedPanel();
        card.Margin = new Padding(5, 2, 4, 7);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(8, 9, 8, 9) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        for (int i = 1; i <= 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14.285f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(SectionTitle("⌁  시스템 상태", 10.5f), 0, 0);
        layout.Controls.Add(StatusRow("게임 창 인식", _sysGameValue), 0, 1);
        layout.Controls.Add(StatusRow("이미지 템플릿", _sysTemplateValue), 0, 2);
        layout.Controls.Add(StatusRow("OCR 기능", _sysOcrValue), 0, 3);
        layout.Controls.Add(StatusRow("키보드 입력", _sysInputValue), 0, 4);
        layout.Controls.Add(StatusRow("와치독", _sysWatchdogValue), 0, 5);
        layout.Controls.Add(StatusRow("텔레그램 알림", _sysTelegramValue), 0, 6);
        layout.Controls.Add(StatusRow("업데이트 서버", _sysUpdateValue), 0, 7);
        var updateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        updateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        updateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _updateStatusValue.Text = "업데이트 확인 중...";
        _updateStatusValue.Dock = DockStyle.Fill;
        _updateStatusValue.ForeColor = Muted;
        _updateStatusValue.Font = new Font("맑은 고딕", 7.6f);
        _updateStatusValue.TextAlign = ContentAlignment.MiddleLeft;
        _updateButton = SmallButton("업데이트", () => _ = CheckForUpdatesAsync(true));
        updateRow.Controls.Add(_updateStatusValue, 0, 0);
        updateRow.Controls.Add(_updateButton, 1, 0);
        layout.Controls.Add(updateRow, 0, 8);
        card.Controls.Add(layout);
        return card;
    }

    private Control StatusRow(string caption, Label value)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        row.Controls.Add(new Label { Text = "●  " + caption, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(190, 212, 234), Font = new Font("맑은 고딕", 7.8f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.ForeColor = Green;
        value.Font = new Font("맑은 고딕", 8.0f, FontStyle.Bold);
        value.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private Control BuildAbyssDungeonGallery()
    {
        var card = BorderedPanel();
        card.Margin = new Padding(4, 2, 4, 5);
        _dungeonGallery = card;
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14, 8, 14, 10) };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.Controls.Add(SectionTitle("🎮  어비스 던전 선택", 11f), 0, 0);
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        string[] names = { "허상의 정박지", "광기의 동굴", "흩어진 물길" };
        string[] desc = { "고요 속에 가라앉은 진실", "뒤틀린 광기의 울림", "흩어진 흐름이 이어지는 곳" };
        string[] files = { "hallucination_anchorage.png", "madness_cave.png", "scattered_waterway.png" };

        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var tile = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 2, 5, 2), BackColor = CardBg2, Cursor = Cursors.Hand, Tag = index };
            tile.Paint += (_, e) =>
            {
                bool selected = tile.Tag is int idx && idx == _abyssDungeon.SelectedIndex;
                using var pen = new Pen(selected ? Color.FromArgb(0, 190, 255) : Line, selected ? 4f : 1f);
                var r = tile.ClientRectangle; r.Width = Math.Max(0, r.Width - 1); r.Height = Math.Max(0, r.Height - 1);
                if (r.Width > 0 && r.Height > 0) e.Graphics.DrawRectangle(pen, r);
            };
            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(5, 19, 34), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 4) };
            try
            {
                string path = Path.Combine(UpdateManager.FindPackageRoot(), "FishingAutomation", "abyss", "templates", files[i]);
                if (File.Exists(path)) { using var source = Image.FromFile(path); picture.Image = new Bitmap(source); }
            }
            catch { }
            var name = new Label { Text = names[i], Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("맑은 고딕", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            var description = new Label { Text = desc[i], Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("맑은 고딕", 7.6f), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            var choose = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(8, 30, 51), ForeColor = Color.FromArgb(195, 220, 245), Font = new Font("맑은 고딕", 8.3f), Cursor = Cursors.Hand, Tag = index };
            choose.FlatAppearance.BorderColor = Line;
            Action select = () =>
            {
                if (AnyRunning || _activeMode is not null) return;
                _mode.SelectedIndex = 2;
                _abyssDungeon.SelectedIndex = index;
                UpdateDashboard();
            };
            tile.Click += (_, _) => select(); picture.Click += (_, _) => select(); name.Click += (_, _) => select(); description.Click += (_, _) => select(); choose.Click += (_, _) => select();
            inner.Controls.Add(picture, 0, 0); inner.Controls.Add(name, 0, 1); inner.Controls.Add(description, 0, 2); inner.Controls.Add(choose, 0, 3);
            tile.Controls.Add(inner);
            _dungeonCards.Add(tile); _dungeonButtons.Add(choose); row.Controls.Add(tile, i, 0);
        }
        outer.Controls.Add(row, 0, 1); card.Controls.Add(outer); return card;
    }

    private Control BuildBottomBar()
    {
        var bar = BorderedPanel(CardBg2);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10, 7, 8, 7) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _logToggle = new Button { Text = "▤  로그 보기 (최근 기록)", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = CardBg2, ForeColor = Color.FromArgb(190, 215, 239), Font = new Font("맑은 고딕", 9f), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
        _logToggle.FlatAppearance.BorderSize = 0;
        _logToggle.Click += (_, _) => { _logExpanded = !_logExpanded; ApplyLogVisibility(); };
        _mainActionButton = new Button { Text = "▶  자동 실행 시작", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White, Font = new Font("맑은 고딕", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
        _mainActionButton.FlatAppearance.BorderColor = Color.FromArgb(63, 162, 255);
        _mainActionButton.Click += (_, _) => { if (AnyRunning) StopSelected(); else StartSelected(); };
        layout.Controls.Add(_logToggle, 0, 0); layout.Controls.Add(_mainActionButton, 1, 0); bar.Controls.Add(layout); return bar;
    }

    private void ApplyLogVisibility()
    {
        if (_referenceDashboard is null) return;
        _referenceDashboard.SetLogExpanded(_logExpanded);
        _logToggle.Text = _logExpanded ? "로그 접기" : "로그 보기 (최근 기록)";
    }

    private void ToggleMini(bool enabled)
    {
        if (_mini == enabled) return;
        SuspendLayout();
        _mini = enabled;
        if (enabled)
        {
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            _fullSize = Size;
            MinimumSize = new Size(234, 137);
            Size = new Size(312, 156);
        }
        else
        {
            MinimumSize = new Size(Math.Min(940, Screen.FromControl(this).WorkingArea.Width), Math.Min(700, Screen.FromControl(this).WorkingArea.Height));
            Size = _fullSize;
        }
        _fullView.Visible = !enabled;
        _miniView.Visible = enabled;
        TopMost = enabled;
        if (enabled) _miniView.BringToFront(); else _fullView.BringToFront();
        ResumeLayout(true);
    }

    private void TrackProgress(string mode, string text)
    {
        _progressLogPreview.Text = text.Length > 150 ? text[..150] + "..." : text;
        if (text.Contains("판 시작")) { _stageStartedAt = _restartAt = null; _stageProgress = 10; SetStatus("입장 대기", Blue); }
        if (text.Contains("abyss_dungeon_hallucination_anchorage")) { _stageProgress = Math.Max(_stageProgress, 30); _recentDetectText = "허상의 정박지.png"; }
        if (text.Contains("abyss_dungeon_madness_cave")) { _stageProgress = Math.Max(_stageProgress, 30); _recentDetectText = "광기의 동굴.png"; }
        if (text.Contains("abyss_dungeon_scattered_waterway")) { _stageProgress = Math.Max(_stageProgress, 30); _recentDetectText = "흩어진 물길.png"; }
        if (text.Contains("abyss_menu")) _recentDetectText = "어비스 메뉴";
        if (text.Contains("입장")) _stageProgress = Math.Max(_stageProgress, 45);
        if (text.Contains("abyss_touch_screen")) _stageProgress = Math.Max(_stageProgress, 65);
        if (text.Contains("abyss_treasure_chest")) { _stageProgress = Math.Max(_stageProgress, 82); _recentDetectText = "보물상자"; }
        if (text.Contains("abyss_exit") || text.Contains("abyss_leave_dungeon")) _stageProgress = Math.Max(_stageProgress, 92);

        if (text.Contains("대기:"))
        {
            if (text.Contains("abyss_touch_screen"))
            {
                _stageStartedAt = DateTime.Now;
                var match = System.Text.RegularExpressions.Regex.Match(text, @"/ (\d+)s");
                _stageLimit = match.Success ? int.Parse(match.Groups[1].Value) : 600;
                SetStatus("던전 진행 중 · 클리어 화면 대기", Blue);
            }
            else if (text.Contains("abyss_treasure_chest")) { _stageStartedAt = null; SetStatus("보상 확인 · 보물상자 대기", Blue); }
            else if (text.Contains("abyss_exit") || text.Contains("abyss_leave_dungeon")) SetStatus("퇴장 버튼 확인 중", Blue);
            else SetStatus(text.Split(']')[0].TrimStart('['), Blue);
        }
        if (text.Contains("[자동복구]")) SetStatus("자동 복구 중", Color.Orange);
        if (text.Contains("[어비스 타임아웃] 퇴장 입력 시작")) { _timeoutExits++; _stageStartedAt = null; SetStatus("10분 제한 초과 · 퇴장 중", Color.Orange); }
        if (text.Contains("나가기 클릭 완료")) { _stageStartedAt = null; SetStatus("던전 밖 복귀 확인 중", Blue); }
        if (text.Contains("던전 밖 복귀 확인 완료")) { _stageStartedAt = null; _stageProgress = 100; SetStatus("완료 · 다음 입장 준비", Green); }
        if (text.Contains("강제 퇴장 완료"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"-> (\d+)초");
            _restartAt = DateTime.Now.AddSeconds(match.Success ? int.Parse(match.Groups[1].Value) : 30);
            SetStatus("재시작 대기", Color.Orange);
        }
        if (text.Contains("[어비스 퇴장 상태 초기화]"))
        {
            _stageStartedAt = _restartAt = null;
            if (text.Contains("클리어 확인")) SetStatus("클리어 화면 확인 · 결과 처리", Blue);
        }
        if (text.Contains("판 완료")) { _stageStartedAt = null; _stageProgress = 100; SetStatus("완료 · 다음 입장 준비", Green); }
    }

    private void UpdateDashboard()
    {
        if (_referenceDashboard is null) return;
        bool idle = !AnyRunning && _activeMode is null;
        string displayMode = _activeMode ?? SelectedMode;
        bool abyss = displayMode == "어비스";
        bool dungeon = displayMode == "던전";
        _currentModeValue.Text = displayMode;
        _currentDungeonValue.Text = abyss ? SelectedAbyssDungeon : dungeon ? SelectedDungeonDestination : displayMode == "페카 심층" ? SelectedPeacaDestination : displayMode == "페카 심층" ? SelectedPeacaDestination : "—";
        string currentStepText = string.IsNullOrWhiteSpace(_statusValue.Text) ? "준비" : _statusValue.Text;
        if (abyss && currentStepText.StartsWith("어비스 준비 완료", StringComparison.Ordinal))
            currentStepText = $"어비스 준비 완료\n{SelectedAbyssDungeon}";
        _currentStepValue.Text = currentStepText;
        _recentDetectValue.Text = _recentDetectText;
        _timeoutValue.Text = abyss ? _timeoutExits.ToString() : "0";

        foreach (var card in _dungeonCards) card.Invalidate();
        for (int i = 0; i < _dungeonButtons.Count; i++)
        {
            bool selected = i == _abyssDungeon.SelectedIndex;
            _dungeonButtons[i].Enabled = idle;
            _dungeonButtons[i].Text = selected ? "●  선택됨" : "○  선택하기";
            _dungeonButtons[i].BackColor = selected ? Color.FromArgb(11, 72, 132) : Color.FromArgb(8, 30, 51);
            _dungeonButtons[i].ForeColor = selected ? Color.White : Color.FromArgb(190, 215, 239);
        }

        if (_restartAt.HasValue && DungeonRunning)
            _stageTime.Text = $"다시 시작까지 {Math.Max(0, (int)Math.Ceiling((_restartAt.Value - DateTime.Now).TotalSeconds))}초";
        else if (_stageStartedAt.HasValue && DungeonRunning)
        {
            var elapsed = DateTime.Now - _stageStartedAt.Value;
            _stageTime.Text = $"경과 {elapsed:mm\\:ss} · 제한까지 {Math.Max(0, _stageLimit - (int)elapsed.TotalSeconds)}초";
        }
        else _stageTime.Text = abyss ? "10분 초과 시 자동 퇴장 · 실패 시 Smart Recovery" : "F9 시작 · F10 정지 · F8 테스트";

        _miniInfo.Text = $"{(abyss ? SelectedAbyssDungeon : dungeon ? SelectedDungeonDestination : displayMode == "페카 심층" ? SelectedPeacaDestination : displayMode)} · {_stageTime.Text}";
        _readyBadge.Text = AnyRunning ? "●  매크로 실행 중" : "●  매크로 준비됨";
        _readyBadge.ForeColor = AnyRunning ? Color.FromArgb(83, 192, 255) : Color.White;
        if (_mainActionButton is not null)
        {
            _mainActionButton.Text = AnyRunning ? "■  자동 실행 정지" : "▶  자동 실행 시작";
            _mainActionButton.BackColor = AnyRunning ? Color.FromArgb(130, 41, 57) : Accent;
        }
        _progress.Value = Math.Max(0, Math.Min(100, _stageProgress));

        var ns = _notifier.Settings;
        bool telegramConfigured = ns.Enabled && !string.IsNullOrWhiteSpace(ns.BotToken) && !string.IsNullOrWhiteSpace(ns.ChatId);
        bool telegramReady = telegramConfigured && _diagTelegramConnected == true;
        _telegramQuickValue.Text = telegramReady ? "ON" : telegramConfigured ? "CHECK" : "OFF";
        _sysTelegramValue.Text = !telegramConfigured ? "설정 필요" : _diagTelegramConnected is null ? "확인 중" : telegramReady ? "정상" : "연결 실패";
        _sysTelegramValue.ForeColor = telegramReady ? Green : telegramConfigured ? Color.Orange : Color.Salmon;

        int windows = WindowTools.EnumerateVisibleWindows().Count;
        bool gameReady = windows > 0;
        bool inputReady = _fishingBot.InputReady;
        bool templatesReady = TemplatesReady();
        bool ocrReady = _diagOcrAvailable == true;
        _sysGameValue.Text = gameReady ? "정상" : "확인 필요";
        _sysGameValue.ForeColor = gameReady ? Green : Color.Orange;
        _sysInputValue.Text = inputReady ? "정상" : "확인 필요";
        _sysInputValue.ForeColor = inputReady ? Green : Color.Orange;
        _sysWatchdogValue.Text = "작동 중"; _sysWatchdogValue.ForeColor = Green;
        _sysOcrValue.Text = _diagOcrAvailable is null ? "확인 중" : ocrReady ? "정상" : "확인 필요";
        _sysOcrValue.ForeColor = ocrReady ? Green : Color.Orange;
        _sysTemplateValue.Text = templatesReady ? "정상" : "확인 필요";
        _sysTemplateValue.ForeColor = templatesReady ? Green : Color.Orange;
        _sysUpdateValue.Text = _diagUpdateServer is null ? "확인 중" : _diagUpdateServer == true ? "정상" : "연결 실패";
        _sysUpdateValue.ForeColor = _diagUpdateServer == true ? Green : Color.Orange;

        SetChip(_topGameChip, gameReady, "게임 감지됨", "게임 확인 필요");
        SetChip(_topOcrChip, ocrReady, "OCR 준비됨", _diagOcrAvailable is null ? "OCR 확인 중" : "OCR 확인 필요");
        SetChip(_topTemplateChip, templatesReady, "템플릿 매칭 준비됨", "템플릿 확인 필요");
        SetChip(_topInputChip, inputReady, "키보드 입력 준비됨", "키보드 입력 확인 필요");
        _referenceDashboard.AccessibleDescription = $"현재 모드: {_currentModeValue.Text}, 단계: {_currentStepValue.Text}, 입력: {_sysInputValue.Text}";
        _referenceDashboard.Invalidate();
    }

    private void SetChip(Label label, bool ready, string readyText, string notReadyText)
    {
        label.Text = (ready ? "●  " : "●  ") + (ready ? readyText : notReadyText);
        label.ForeColor = ready ? Green : Color.Orange;
    }

    private bool TemplatesReady()
    {
        try
        {
            string package = UpdateManager.FindPackageRoot();
            return File.Exists(Path.Combine(package, "FishingAutomation", "templates", "hook.png"))
                && File.Exists(Path.Combine(package, "FishingAutomation", "templates", "gauge.png"))
                && File.Exists(Path.Combine(package, "FishingAutomation", "abyss", "templates", "menu.png"));
        }
        catch { return false; }
    }

    private void OpenPackagePath(string relative)
    {
        try
        {
            string path = Path.Combine(UpdateManager.FindPackageRoot(), relative);
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }

    private void OpenPhoneAlertSetup()
    {
        try
        {
            string package = UpdateManager.FindPackageRoot();
            string file = Path.Combine(package, "SETUP_PHONE_ALERT.cmd");
            if (File.Exists(file)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, WorkingDirectory = package, UseShellExecute = true });
        }
        catch { }
    }

    private void OpenLogFolder()
    {
        try
        {
            string package = UpdateManager.FindPackageRoot();
            string release = Path.Combine(package, "release");
            string path = Directory.Exists(release) ? release : package;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }
}


