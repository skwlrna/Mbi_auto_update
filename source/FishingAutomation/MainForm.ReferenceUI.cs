using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private ReferenceDashboard? _referenceDashboard;

    private void BuildLayout()
    {
        _mode.Items.AddRange(new object[] { "낚시", "던전", "어비스", "페카 심층", "가공", "채집", "제작", "스케줄러" });
        _mode.SelectedIndex = 0;
        _dungeonDestination.Items.AddRange(new object[] { "현재 위치" });
        _dungeonDestination.SelectedIndex = 0;
        _abyssDungeon.Items.AddRange(new object[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" });
        _abyssDungeon.SelectedIndex = 0;
        _fullView = new Panel { Dock = DockStyle.Fill, BackColor = WindowBg };
        _referenceDashboard = new ReferenceDashboard(this) { Dock = DockStyle.Fill };
        _fullView.Controls.Add(_referenceDashboard);
        Controls.Add(_fullView);

        _miniView = new Panel { Dock = DockStyle.Fill, BackColor = NavBg, Padding = new Padding(12), Visible = false };
        var mini = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        mini.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        mini.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        mini.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        mini.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var title = new Label { Text = "Mabi_Auto  ·  미니 모드", Dock = DockStyle.Fill, ForeColor = TitleText, TextAlign = ContentAlignment.MiddleLeft };
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) DragReferenceWindow(); };
        mini.Controls.Add(title, 0, 0);
        foreach (var label in new[] { _miniStatus, _miniInfo })
        {
            label.Dock = DockStyle.Fill;
            label.ForeColor = TitleText;
            label.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
        }
        mini.Controls.Add(_miniStatus, 0, 1);
        mini.Controls.Add(_miniInfo, 0, 2);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        actions.Controls.Add(SmallButton("정지 (F10)", StopSelected), 0, 0);
        actions.Controls.Add(SmallButton("전체 화면", () => ToggleMini(false)), 1, 0);
        actions.Controls.Add(SmallButton("종료", Close), 2, 0);
        mini.Controls.Add(actions, 0, 3);
        _miniView.Controls.Add(mini);
        Controls.Add(_miniView);
        _fullView.BringToFront();
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private void DragReferenceWindow()
    {
        if (WindowState != FormWindowState.Normal) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    private bool HandleReferenceResize(ref Message m)
    {
        if (m.Msg != 0x0084 || WindowState != FormWindowState.Normal) return false;
        long point = m.LParam.ToInt64();
        var p = PointToClient(new Point(unchecked((short)point), unchecked((short)(point >> 16))));
        int edge = Math.Max(5, DeviceDpi / 20);
        bool left = p.X < edge, right = p.X >= ClientSize.Width - edge;
        bool top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
        int hit = top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 0;
        if (hit == 0) return false;
        m.Result = (IntPtr)hit;
        return true;
    }

    // Every caption and value is drawn from live controls/state. Only the supplied
    // banner and dungeon artwork are sampled from the reference; no status text is baked in.
    private sealed partial class ReferenceDashboard : Panel
    {
        private readonly MainForm _owner;
        private readonly Image _art;
        private readonly Image?[] _dungeonPreviews;
        private readonly List<(Control Control, RectangleF Bounds)> _positions = new();
        private readonly List<ReferenceButton> _gallery = new();
        private readonly List<ReferenceButton> _modeTabs = new();
        private readonly List<ReferenceButton> _navigationButtons = new();
        private readonly List<ReferenceButton> _homeLinks = new();
        private string _selectedNavigation = "홈";
        private bool _showHome = true;
        private readonly ToolTip _tips = new();
        private ContextMenuStrip? _openMenu;
        private readonly Font _baseFont = new("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        // The dashboard paints many captions every refresh. Reuse GDI fonts by
        // integer pixel size instead of creating one for every text draw.
        private readonly Dictionary<(int Pixels, bool Bold), Font> _textFonts = new();
        private readonly Color _text = TitleText;
        private readonly Color _cyan = Blue;
        private readonly Color _green = Green;
        private readonly Color _line = Line;
        private float ScaleX => Math.Max(1, Width) / 1448f;
        private float ScaleY => Math.Max(1, Height) / 1086f;
        private string EffectiveMode => _owner._activeMode ?? _owner._mode.SelectedItem?.ToString() ?? "낚시";
        private bool ShouldShowAbyssDungeonPicker =>
            string.Equals(EffectiveMode, "어비스", StringComparison.Ordinal);
        private bool ShouldShowProductionPage => _owner._productionPageMode is "가공" or "채집" or "제작" or "스케줄러";

        public ReferenceDashboard(MainForm owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("FishingAutomation.ReferenceArtwork.png");
            if (stream is null)
            {
                _art = new Bitmap(1448, 1086);
                using var artGraphics = Graphics.FromImage(_art);
                artGraphics.Clear(WindowBg);
            }
            else
            {
                using var original = Image.FromStream(stream);
                _art = new Bitmap(original);
            }
            BackColor = WindowBg;

            _dungeonPreviews = new Image?[]
            {
                LoadDungeonPreview("hallucination_anchorage.jpg"),
                LoadDungeonPreview("madness_cave.jpg"),
                LoadDungeonPreview("scattered_waterway.jpg")
            };

            // v70: HOME-only scheduled stop control. Keep it in the unused top-banner area,
            // use one full caption and a dark masked time field so it visually matches the
            // custom-painted dashboard instead of looking like a separate Windows widget.
            var autoStopCheck = new CheckBox
            {
                Text = "자동 정지",
                AccessibleName = "자동 정지",
                Checked = owner._autoStopEnabled,
                AutoSize = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBg,
                ForeColor = _text,
                Font = new Font(_baseFont.FontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point),
                CheckAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
                Padding = new Padding(3, 0, 0, 1)
            };
            autoStopCheck.FlatAppearance.BorderSize = 0;

            var autoStopTime = new MaskedTextBox("00:00")
            {
                AccessibleName = "자동 정지 시간",
                Text = $"{owner._autoStopTime.Hours:00}:{owner._autoStopTime.Minutes:00}",
                TextAlign = HorizontalAlignment.Center,
                BackColor = CardBg2,
                ForeColor = TitleText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(_baseFont.FontFamily, 9.5f, FontStyle.Bold, GraphicsUnit.Point),
                PromptChar = ' ',
                HidePromptOnLeave = false,
                InsertKeyMode = InsertKeyMode.Overwrite,
                RejectInputOnFirstFailure = true,
                ShortcutsEnabled = true,
                AutoSize = false
            };

            void CommitAutoStopTime()
            {
                string[] parts = autoStopTime.Text.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int hour) &&
                    int.TryParse(parts[1], out int minute) &&
                    hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
                {
                    owner.SetAutoStopTime(DateTime.Today.AddHours(hour).AddMinutes(minute));
                    autoStopTime.Text = $"{hour:00}:{minute:00}";
                }
                else
                {
                    autoStopTime.Text = $"{owner._autoStopTime.Hours:00}:{owner._autoStopTime.Minutes:00}";
                }
            }

            autoStopCheck.CheckedChanged += (_, _) => owner.SetAutoStopEnabled(autoStopCheck.Checked);
            autoStopTime.Leave += (_, _) => CommitAutoStopTime();
            autoStopTime.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                CommitAutoStopTime();
                e.SuppressKeyPress = true;
            };
            autoStopTime.GotFocus += (_, _) =>
                autoStopTime.BeginInvoke(new Action(autoStopTime.SelectAll));

            Controls.Add(autoStopCheck);
            Controls.Add(autoStopTime);

            // At the normal 0.65 HOME scale these become roughly 104px + 86px wide,
            // enough for the complete caption and HH:mm without touching nearby cards.
            var autoStopCheckBounds = new RectangleF(1110, 110, 170, 38);
            var autoStopTimeBounds = new RectangleF(1300, 112, 110, 34);
            _positions.Add((autoStopCheck, autoStopCheckBounds));
            _positions.Add((autoStopTime, autoStopTimeBounds));
            autoStopCheck.Bounds = Rectangle.Round(new RectangleF(autoStopCheckBounds.X * ScaleX, autoStopCheckBounds.Y * ScaleY, autoStopCheckBounds.Width * ScaleX, autoStopCheckBounds.Height * ScaleY));
            autoStopTime.Bounds = Rectangle.Round(new RectangleF(autoStopTimeBounds.X * ScaleX, autoStopTimeBounds.Y * ScaleY, autoStopTimeBounds.Width * ScaleX, autoStopTimeBounds.Height * ScaleY));

            AddButton("최소화", new(1247, 8, 64, 48), () => owner.WindowState = FormWindowState.Minimized, "min", "chrome");
            AddButton("최대화 / 복원", new(1312, 8, 64, 48), () =>
            {
                owner.MaximizedBounds = Screen.FromControl(owner).WorkingArea;
                owner.WindowState = owner.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            }, "max", "chrome");
            AddButton("닫기", new(1377, 8, 64, 48), owner.Close, "close", "chrome");
            // Navigations are drawn from the user-provided Mobilay screenshot.
            BuildMobilayNavigation(owner);
            _modeTabs.Add(AddButton("낚시", new(276, 190, 210, 62), () => SelectMode(0), "fish", "mode-tab"));
            _modeTabs.Add(AddButton("던전", new(496, 190, 210, 62), () => SelectMode(1), "swords", "mode-tab"));
            _modeTabs.Add(AddButton("어비스", new(716, 190, 210, 62), () => SelectMode(2), "abyss", "mode-tab"));
            // Mini mode is now available in the Mobilay-style utility rail.

            // The mode cards have no chevron. Their caption area still opens the
            // existing destination selector when a dungeon/abyss mode is active.
            owner._updateButton = AddButton("업데이트 확인", new(1139, 881, 265, 41), () => _ = owner.CheckForUpdatesAsync(true), "", "quiet");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var choose = AddButton("선택하기", new(278 + i * 380, 864, 346, 48), () => SelectDungeon(index), "radio", "choose");
                choose.Tag = i;
                _gallery.Add(choose);
                owner._dungeonButtons.Add(choose);
            }
            // Logs can only be opened from the sidebar. Preserve the legacy
            // detached control so ApplyLogVisibility remains safe.
            owner._logToggle = new Button { Visible = false };
            owner._mainActionButton = AddButton("자동 실행 시작", new(256, 962, 1166, 75), () =>
            {
                if (owner.AnyRunning) owner.StopSelected(); else owner.StartSelected();
            }, "play", "action");
            owner._logView = owner.BuildLogPanel();
            owner._logView.Dock = DockStyle.None;
            owner._logView.Visible = false;
            Controls.Add(owner._logView);
            _positions.Add((owner._logView, new(256, 659, 1166, 278)));
            owner._alteringPage = new ProductionPage(owner, true);
            owner._gatheringPage = new ProductionPage(owner, false);
            owner._craftingPage = new CraftingPage(owner);
            owner._schedulerPage = new SchedulerPage(owner);
            foreach (Control page in new Control[] { owner._alteringPage, owner._gatheringPage, owner._craftingPage, owner._schedulerPage })
            {
                Controls.Add(page);
                var bounds = new RectangleF(256, 81, 1166, 858);
                _positions.Add((page, bounds));
                page.Bounds = Rectangle.Round(new RectangleF(bounds.X * ScaleX, bounds.Y * ScaleY, bounds.Width * ScaleX, bounds.Height * ScaleY));
            }
            UpdateAbyssDungeonPickerVisibility();
            AccessibleName = "Mabi Auto 대시보드";
        }

        public void NavigateToMode(int mode) => SelectMode(mode);

        private void SelectMode(int mode)
        {
            _owner.SafeUiAction("현재 모드 선택", () =>
            {
                if (_owner.AnyRunning || _owner._activeMode is not null) return;
                if (mode < 0 || mode >= _owner._mode.Items.Count) return;
                _showHome = false;
                // Log diagnostics is a separate destination, not an overlay
                // that should remain above automation, production or scheduling.
                if (_owner._logExpanded)
                {
                    _owner._logExpanded = false;
                    _owner.ApplyLogVisibility();
                }
                _selectedNavigation = mode == 7 ? "스케줄러" : mode is 4 or 5 or 6 ? "생활 생산" : mode is 1 or 2 or 3 ? "던전 / 어비스" : "자동화 실행";
                _owner._productionPageMode = mode == 4 ? "가공" : mode == 5 ? "채집" : mode == 6 ? "제작" : mode == 7 ? "스케줄러" : null;
                _owner._mode.SelectedIndex = mode;
                UpdateAbyssDungeonPickerVisibility();
                if (_owner._productionPageMode == "가공")
                    _ = _owner._alteringPage.LoadCatalogAsync();
                else if (_owner._productionPageMode == "채집")
                    _ = _owner._gatheringPage.LoadCatalogAsync();
                else if (_owner._productionPageMode == "제작")
                    _ = _owner._craftingPage.LoadCatalogAsync();
                else if (_owner._productionPageMode == "스케줄러")
                    _owner._schedulerPage.RefreshSnapshot();
                _owner.UpdateDashboard();
            });
        }

        private void SelectRegularDungeon(int index)
        {
            _owner.SafeUiAction("던전 선택", () =>
            {
                if (_owner.AnyRunning || _owner._activeMode is not null) return;
                if (index < 0 || index >= _owner._dungeonDestination.Items.Count) return;
                _owner._mode.SelectedIndex = 1;
                _owner._dungeonDestination.SelectedIndex = index;
                _owner.UpdateDashboard();
            });
        }

        private void SelectDungeon(int index)
        {
            _owner.SafeUiAction("어비스 던전 선택", () =>
            {
                if (_owner.AnyRunning || _owner._activeMode is not null) return;
                if (index < 0 || index >= _owner._abyssDungeon.Items.Count) return;
                _owner._mode.SelectedIndex = 2;
                _owner._abyssDungeon.SelectedIndex = index;
                _owner.UpdateDashboard();
            });
        }

        private void ShowModeMenu()
        {
            if (_owner.AnyRunning || _owner._activeMode is not null) return;
            ShowMenu(new[] { "낚시", "던전", "어비스" },
                index => SelectMode(index), 935, 252);
        }

        private void ShowDungeonMenu()
        {
            if (_owner.AnyRunning || _owner._activeMode is not null) return;

            string mode = _owner._mode.SelectedItem?.ToString() ?? "";
            if (string.Equals(mode, "던전", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "현재 위치" }, SelectRegularDungeon, 933, 245);
                return;
            }

            if (string.Equals(mode, "페카 심층", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "페카 심층 1-1", "페카 심층 2-1" }, index =>
                {
                    if (_owner.AnyRunning || _owner._activeMode is not null) return;
                    _owner._selectedPeacaRoute = index == 1 ? "peaca_d2_1" : "peaca_d1_1";
                    _owner.UpdateDashboard();
                }, 933, 245);
                return;
            }

            if (string.Equals(mode, "어비스", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" }, SelectDungeon, 933, 245);
            }
        }

        private void ShowMenu(string[] captions, Action<int> action, int x, int y)
        {
            _owner.SafeUiAction("선택 메뉴 열기", () =>
            {
                // Disposing a ContextMenuStrip synchronously from its Closed event can
                // race the final ToolStrip click/paint messages. Keep only one menu open
                // and let the closed menu be collected after WinForms has finished with it.
                if (_openMenu is { IsDisposed: false, Visible: true })
                    _openMenu.Close();

                var menu = new ContextMenuStrip
                {
                    BackColor = CardBg,
                    ForeColor = _text,
                    Font = Font,
                    ShowImageMargin = false
                };
                _openMenu = menu;

                for (int i = 0; i < captions.Length; i++)
                {
                    int index = i;
                    menu.Items.Add(captions[i], null, (_, _) => _owner.SafeUiAction("선택 메뉴 항목", () => action(index)));
                }

                menu.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_openMenu, menu)) _openMenu = null;
                };

                menu.Show(this, new Point((int)(x * ScaleX), (int)(y * ScaleY)));
            });
        }

        private ReferenceButton AddButton(string text, RectangleF bounds, Action action, string icon, string kind)
        {
            var button = new ReferenceButton(this, icon, kind)
            {
                Text = text, AccessibleName = text, Cursor = Cursors.Hand,
                TabStop = kind != "chrome",
                BackColor = CardBg, ForeColor = TitleText,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
            };
            if (kind == "chrome") button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => _owner.SafeUiAction(text, action);
            Controls.Add(button);
            _positions.Add((button, bounds));
            if (kind == "nav") _navigationButtons.Add(button);
            return button;
        }

        public void UpdateAbyssDungeonPickerVisibility()
        {
            bool showButtons = !_showHome && ShouldShowAbyssDungeonPicker && !_owner._logExpanded;
            foreach (var button in _gallery) button.Visible = showButtons;
            if (_owner._alteringPage is not null)
            {
                bool production = ShouldShowProductionPage;
                foreach (var tab in _modeTabs) tab.Visible = !_showHome && !production;
                _owner._alteringPage.Visible = production && _owner._productionPageMode == "가공";
                _owner._gatheringPage.Visible = production && _owner._productionPageMode == "채집";
                _owner._craftingPage.Visible = production && _owner._productionPageMode == "제작";
                _owner._schedulerPage.Visible = production && _owner._productionPageMode == "스케줄러";
                foreach (var (control, bounds) in _positions)
                {
                    if (control is ReferenceButton link && _homeLinks.Contains(link))
                    {
                        control.Visible = _showHome && !_owner._logExpanded;
                        continue;
                    }
                    // The fixed bottom "자동 실행 시작" remains interactive on every
                    // production/scheduler page. Previously it was hidden while the
                    // painted dashboard still suggested a start control existed.
                    if (control is ProductionPage || control is CraftingPage || control is SchedulerPage || control == _owner._logView || control == _owner._logToggle || control == _owner._mainActionButton || _gallery.Contains(control as ReferenceButton ?? null!)) continue;
                    if (bounds.X >= 219 && bounds.Y >= 81) control.Visible = !_showHome && !production;
                }
                _owner._mainActionButton.Visible = !_showHome || _owner.AnyRunning;
                _owner._logToggle.Visible = false;
                _owner._updateButton.Visible = _showHome;
                if (_owner._logExpanded) _owner._logView.BringToFront();
            }
            // Each WinForms owner-drawn button keeps its own paint surface.
            // Explicitly repaint previous choices when the active page changes.
            foreach (var nav in _navigationButtons) nav.Invalidate();
            foreach (var tab in _modeTabs) tab.Invalidate();
            foreach (var option in _gallery) option.Invalidate();
            Invalidate();
        }

        public void SetLogExpanded(bool expanded)
        {
            _owner._logView.Visible = expanded;
            UpdateAbyssDungeonPickerVisibility();
            if (expanded) _owner._logView.BringToFront();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_positions is null) return;
            float sx = ScaleX, sy = ScaleY;
            SuspendLayout();
            try
            {
                foreach (var (control, r) in _positions)
                {
                    var bounds = Rectangle.Round(new RectangleF(r.X * sx, r.Y * sy, r.Width * sx, r.Height * sy));
                    // Avoid repeatedly invalidating unchanged child surfaces.
                    if (control.Bounds != bounds) control.Bounds = bounds;
                }
            }
            finally { ResumeLayout(true); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            float x = e.X / ScaleX, y = e.Y / ScaleY;
            if (y < 65) { _owner.DragReferenceWindow(); return; }
            // Clicking the painted mode-card heading replaces the old down-arrow
            // hit target, so removing its icon doesn't remove dungeon selection.
            if (!_showHome && !ShouldShowProductionPage && !_owner._logExpanded &&
                x >= 256 && x <= 996 && y >= 273 && y <= 396 &&
                EffectiveMode is "던전" or "어비스" or "페카 심층")
            {
                ShowDungeonMenu();
                return;
            }
            if (!_showHome && ShouldShowAbyssDungeonPicker && !_owner._logExpanded && y >= 731 && y <= 922 && x >= 270 && x < 1412)
                SelectDungeon(Math.Clamp((int)((x - 270) / 380), 0, 2));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y / ScaleY < 65)
            {
                _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                _owner.WindowState = _owner.WindowState == FormWindowState.Normal ? FormWindowState.Maximized : FormWindowState.Normal;
            }
        }

        private static GraphicsPath Round(RectangleF r, float radius = 8)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void Card(Graphics g, RectangleF r, bool selected = false)
        {
            using var path = Round(r, 13);
            using var surface = new SolidBrush(selected ? AccentSoft : CardBg);
            g.FillPath(surface, path);
            using var pen = new Pen(selected ? _cyan : _line, selected ? 2 : 1);
            g.DrawPath(pen, path);
        }

        private void TextAt(Graphics g, string? text, RectangleF r, float size = 18, bool bold = false, Color? color = null, StringAlignment align = StringAlignment.Near, bool multiline = false)
        {
            // Convert the layout rectangle to device pixels, then draw without scaling glyphs.
            var points = new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom) };
            using var transform = g.Transform;
            transform.TransformPoints(points);
            var bounds = Rectangle.FromLTRB((int)Math.Round(points[0].X), (int)Math.Round(points[0].Y),
                (int)Math.Round(points[1].X), (int)Math.Round(points[1].Y));
            float scale = Math.Min(Math.Abs(transform.Elements[0]), Math.Abs(transform.Elements[3]));
            int pixels = (int)Math.Max(size >= 18 ? 13f : 12f, Math.Round(size * scale));
            var key = (pixels, bold);
            if (!_textFonts.TryGetValue(key, out var font))
            {
                font = new Font(_baseFont.FontFamily, pixels,
                    bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                _textFonts.Add(key, font);
            }
            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
                flags |= align == StringAlignment.Center ? TextFormatFlags.HorizontalCenter
                    : align == StringAlignment.Far ? TextFormatFlags.Right : TextFormatFlags.Left;
                flags |= multiline || (text ?? "").Contains('\n')
                    ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(g, text ?? "", font, bounds, color ?? _text, flags);
            }
            finally { g.Restore(state); }
        }

        private void Artwork(Graphics g, RectangleF destination, Rectangle source)
        {
            g.DrawImage(_art, destination, source, GraphicsUnit.Pixel);
        }

        private void Dot(Graphics g, float x, float y, float radius, Color color)
        {
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        }

        private static Image? LoadDungeonPreview(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "abyss", "previews", fileName);
                if (!File.Exists(path)) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }
            catch { return null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.ScaleTransform(ScaleX, ScaleY);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var bg = new SolidBrush(WindowBg)) g.FillRectangle(bg, 0, 0, 1448, 1086);
            using (var nav = new SolidBrush(NavBg)) g.FillRectangle(nav, 0, 100, 232, 986);
            using (var line = new Pen(Line))
            {
                g.DrawLine(line, 0, 100, 1448, 100);
                g.DrawLine(line, 232, 100, 232, 1086);
            }
            TextAt(g, "Mabi Auto", new(34, 12, 232, 63), 32, true, TitleText);
            TextAt(g, UpdateManager.CurrentVersion, new(280, 21, 160, 40), 16, false, Muted);
            TextAt(g, "F9 시작  ·  F10 정지", new(480, 22, 310, 39), 14, false, Muted);
            if (ShouldShowProductionPage) return;
            if (_showHome) PaintHome(g);
            else PaintAutomation(g);
        }

        // Read-only home. Every value is taken from the existing live runtime state,
        // rather than mock statistics or an invented scheduling calendar.
        private void PaintHome(Graphics g) => PaintMobilayHome(g);

        private void PaintAutomation(Graphics g)
        {
            TextAt(g, "자동화 실행", new(278, 91, 800, 60), 34, true, TitleText);
            TextAt(g, "낚시 · 던전 · 어비스 중 실행할 작업을 선택하세요.", new(279, 151, 1000, 30), 18, false, Muted);

            Card(g, new(256, 273, 740, 372));
            TextAt(g, EffectiveMode + " 모드", new(280, 295, 635, 42), 24, true, TitleText);
            TextAt(g, "선택한 모드의 주요 정보", new(280, 348, 610, 30), 17, false, Muted);
            using (var divider = new Pen(Line)) g.DrawLine(divider, 280, 396, 963, 396);
            TextAt(g, "작업 대상", new(280, 417, 168, 36), 17, false, Muted);
            TextAt(g, _owner._currentDungeonValue.Text, new(454, 417, 450, 36), 18, true, TitleText);
            TextAt(g, "현재 단계", new(280, 471, 168, 36), 17, false, Muted);
            TextAt(g, _owner._currentStepValue.Text, new(454, 464, 472, 52), 18, true, TitleText, multiline: true);
            TextAt(g, "최근 인식", new(280, 530, 168, 36), 17, false, Muted);
            TextAt(g, _owner._recentDetectValue.Text, new(454, 529, 469, 40), 18, true, TitleText);
            TextAt(g, "F8 테스트  ·  F9 시작  ·  F10 정지", new(280, 589, 680, 32), 16, false, Muted);

            Card(g, new(1012, 273, 410, 372));
            TextAt(g, "실행 상태", new(1037, 295, 350, 40), 23, true, TitleText);
            Dot(g, 1051, 379, 8, _owner._statusValue.ForeColor);
            TextAt(g, _owner._statusValue.Text, new(1074, 347, 320, 60), 22, true, TitleText);
            TextAt(g, "경과 시간", new(1037, 438, 160, 34), 16, false, Muted);
            TextAt(g, _owner._elapsedValue.Text, new(1200, 433, 196, 42), 19, true, TitleText, StringAlignment.Far);
            TextAt(g, "인식 상태", new(1037, 498, 160, 34), 16, false, Muted);
            TextAt(g, _owner._sysOcrValue.Text, new(1200, 493, 196, 42), 18, true, _owner._sysOcrValue.ForeColor, StringAlignment.Far);
            using (var track = new SolidBrush(CardBg2))
            using (var path = Round(new(1038, 573, 356, 10), 5)) g.FillPath(track, path);
            if (_owner._progress.Value > 0)
            {
                using var fill = new SolidBrush(Blue);
                using var path = Round(new(1038, 573, Math.Max(10, 356 * _owner._progress.Value / 100f), 10), 5);
                g.FillPath(fill, path);
            }

            // Wider content card replaces the redundant Recent Log pane.
            Card(g, new(256, 662, 1166, 275));
            if (ShouldShowAbyssDungeonPicker)
            {
                TextAt(g, "어비스 목적지", new(279, 680, 1100, 35), 21, true, TitleText);
                string[] names = { "허상의 정박지", "광기의 동굴", "흩어진 물길" };
                for (int i = 0; i < 3; i++)
                {
                    int x = 270 + i * 380;
                    Card(g, new(x, 731, 364, 191), i == _owner._abyssDungeon.SelectedIndex);
                    var preview = new RectangleF(x + 7, 739, 350, 76);
                    if (_dungeonPreviews[i] is Image picture) g.DrawImage(picture, preview);
                    else TextAt(g, "미리보기 없음", preview, 14, false, Muted, StringAlignment.Center);
                    TextAt(g, names[i], new(x + 8, 823, 348, 34), 18, true, TitleText, StringAlignment.Center);
                }
            }
            else
            {
                TextAt(g, "현재 작업 안내", new(279, 686, 1065, 40), 21, true, TitleText);
                string guide = EffectiveMode == "낚시" ? "낚시 자동화 · F8로 실제 키 입력을 테스트할 수 있습니다."
                    : "던전 자동화 · 게임 화면과 대상 상태를 확인하고 실행하세요.";
                TextAt(g, guide, new(279, 741, 1065, 58), 18, false, Muted, multiline: true);
                TextAt(g, "로그 기록은 왼쪽 로그 / 진단 메뉴에서 확인할 수 있습니다.", new(279, 830, 1065, 60), 16, false, Muted, multiline: true);
            }
        }

        private void Metric(Graphics g, RectangleF bounds, string caption, string? value, string icon, Color accent)
        {
            Card(g, bounds);
            TextAt(g, caption, new(bounds.X + 20, bounds.Y + 14, bounds.Width - 66, 27), 16, false, Muted);
            Icon(g, icon, new(bounds.Right - 43, bounds.Y + 18, 22, 22), accent);
            TextAt(g, string.IsNullOrWhiteSpace(value) ? "0" : value,
                new(bounds.X + 20, bounds.Y + 49, bounds.Width - 40, 52), 34, true, TitleText);
        }

        private void Info(Graphics g, RectangleF r, string icon, string caption, string value, float iconX)
        {
            Card(g, r);

            // The abyss status strings shown in "현재 단계" are considerably longer
            // than the other info-card values. Give that card a compact header row and
            // the full card width for the value so all three abyss dungeons can show
            // their stage text without clipping.
            if (caption == "현재 단계")
            {
                Icon(g, icon, new(r.X + 11, r.Y + 9, 25, 25), _cyan);
                TextAt(g, caption, new(r.X + 45, r.Y + 7, r.Width - 55, 28), 15);
                TextAt(g, value, new(r.X + 11, r.Y + 36, r.Width - 22, 44), 13, true, TitleText, StringAlignment.Center, multiline: true);
                return;
            }

            Icon(g, icon, new(iconX, r.Y + 24, 35, 35), _cyan);
            float left = iconX + 55;
            float reserved = caption is "현재 모드" or "선택 던전" ? 42 : 10;
            TextAt(g, caption, new(left, r.Y + 12, r.Right - left - reserved, 24), 16);
            TextAt(g, value, new(left, r.Y + 32, r.Right - left - reserved, 50), 18, true, TitleText);
        }

        private void Stat(Graphics g, float y, string icon, string caption, string value, Color c)
        {
            Icon(g, icon, new(844, y + 10, 26, 26), c);
            TextAt(g, caption, new(884, y, 124, 46), 16);
            TextAt(g, string.IsNullOrWhiteSpace(value) ? "0" : value, new(1007, y, 83, 46), 20, true, TitleText, StringAlignment.Far);
            if (y < 550) { using var pen = new Pen(Line); g.DrawLine(pen, 844, y + 48, 1096, y + 48); }
        }

        // Small vector icons remain crisp at arbitrary window sizes and Windows scaling.
        private void Icon(Graphics g, string name, RectangleF r, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 32, r.Height / 32);
            using var pen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var fill = new SolidBrush(color);
            switch (name)
            {
                case "home":
                    g.FillPolygon(fill, new PointF[] { new(1, 15), new(16, 2), new(31, 15), new(26, 15), new(26, 29), new(19, 29), new(19, 20), new(13, 20), new(13, 29), new(6, 29), new(6, 15) }); break;
                case "fish":
                    g.FillEllipse(fill, 9, 8, 21, 16); g.FillPolygon(fill, new PointF[] { new(12, 16), new(1, 7), new(1, 26) });
                    using (var eye = new SolidBrush(NavBg)) g.FillEllipse(eye, 23, 11, 3, 3); break;
                case "swords":
                    g.DrawLine(pen, 5, 3, 27, 28); g.DrawLine(pen, 27, 3, 5, 28); g.DrawLine(pen, 1, 19, 12, 30); g.DrawLine(pen, 20, 30, 31, 19); break;
                case "abyss":
                    g.FillPolygon(fill, new PointF[] { new(16, 0), new(30, 16), new(16, 32), new(2, 16) });
                    using (var hole = new SolidBrush(NavBg)) g.FillEllipse(hole, 10, 10, 12, 12); break;
                case "leaf": g.DrawArc(pen, 4, 3, 25, 25, 90, 270); g.DrawLine(pen, 2, 30, 28, 3); g.DrawLine(pen, 7, 27, 29, 3); break;
                case "settings":
                    g.DrawEllipse(pen, 8, 8, 16, 16); g.DrawEllipse(pen, 13, 13, 6, 6);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 16 + 10 * (float)Math.Cos(a), 16 + 10 * (float)Math.Sin(a), 16 + 15 * (float)Math.Cos(a), 16 + 15 * (float)Math.Sin(a)); } break;
                case "crosshair": g.DrawEllipse(pen, 5, 5, 22, 22); g.DrawLine(pen, 16, 0, 16, 10); g.DrawLine(pen, 16, 22, 16, 32); g.DrawLine(pen, 0, 16, 10, 16); g.DrawLine(pen, 22, 16, 32, 16); break;
                case "map": g.FillPolygon(fill, new PointF[] { new(2, 4), new(10, 1), new(10, 28), new(2, 31) }); g.FillPolygon(fill, new PointF[] { new(13, 2), new(20, 5), new(20, 31), new(13, 28) }); g.FillPolygon(fill, new PointF[] { new(23, 5), new(30, 1), new(30, 27), new(23, 31) }); break;
                case "calendar-clock":
                    g.DrawRectangle(pen, 2, 4, 26, 23); g.DrawLine(pen, 2, 11, 28, 11);
                    g.DrawLine(pen, 9, 0, 9, 8); g.DrawLine(pen, 22, 0, 22, 8);
                    g.FillEllipse(fill, 7, 16, 3, 3); g.FillEllipse(fill, 14, 16, 3, 3);
                    g.DrawEllipse(pen, 19, 19, 12, 12); g.DrawLine(pen, 25, 21, 25, 25); g.DrawLine(pen, 25, 25, 28, 27); break;
                case "scan-eye":
                    g.DrawLine(pen, 1, 10, 1, 2); g.DrawLine(pen, 1, 2, 9, 2);
                    g.DrawLine(pen, 23, 2, 31, 2); g.DrawLine(pen, 31, 2, 31, 10);
                    g.DrawLine(pen, 1, 22, 1, 30); g.DrawLine(pen, 1, 30, 9, 30);
                    g.DrawLine(pen, 23, 30, 31, 30); g.DrawLine(pen, 31, 30, 31, 22);
                    g.DrawEllipse(pen, 9, 11, 14, 11); g.FillEllipse(fill, 14, 14, 4, 4); break;
                case "list": for (int i = 0; i < 3; i++) { g.FillEllipse(fill, 1, 4 + i * 10, 4, 4); g.DrawLine(pen, 11, 6 + i * 10, 29, 6 + i * 10); } break;
                case "image": g.DrawRectangle(pen, 2, 3, 28, 26); g.FillEllipse(fill, 7, 7, 6, 6); g.DrawLines(pen, new PointF[] { new(3, 24), new(12, 17), new(17, 21), new(23, 14), new(29, 21) }); break;
                case "play": g.FillPolygon(fill, new PointF[] { new(5, 2), new(28, 16), new(5, 30) }); break;
                case "stats": for (int i = 0; i < 4; i++) g.FillRectangle(fill, i * 8, 26 - (i % 3 + 1) * 7, 4, (i % 3 + 1) * 7 + 3); break;
                case "check": g.DrawLines(pen, new PointF[] { new(3, 15), new(12, 25), new(29, 7) }); break;
                case "close": g.DrawLine(pen, 6, 6, 26, 26); g.DrawLine(pen, 26, 6, 6, 26); break;
                case "refresh": g.DrawArc(pen, 4, 4, 24, 24, 35, 285); g.DrawLines(pen, new PointF[] { new(27, 2), new(28, 13), new(17, 11) }); break;
                case "clock": g.DrawEllipse(pen, 3, 3, 26, 26); g.DrawLines(pen, new PointF[] { new(16, 8), new(16, 17), new(23, 21) }); break;
                case "pulse": g.DrawLines(pen, new PointF[] { new(1, 17), new(8, 17), new(13, 2), new(18, 29), new(23, 17), new(31, 17) }); break;
                case "game": g.DrawArc(pen, 1, 7, 30, 21, 170, 205); g.DrawLines(pen, new PointF[] { new(1, 18), new(1, 28), new(8, 29), new(12, 23), new(20, 23), new(24, 29), new(31, 28), new(31, 18) }); g.DrawLine(pen, 7, 12, 7, 20); g.DrawLine(pen, 3, 16, 11, 16); g.FillEllipse(fill, 23, 12, 4, 4); g.FillEllipse(fill, 19, 17, 4, 4); break;
                case "ocr": g.DrawLine(pen, 5, 4, 27, 4); g.DrawLine(pen, 16, 4, 16, 29); break;
                case "keyboard": g.DrawRectangle(pen, 1, 5, 30, 22); for (int y = 11; y <= 17; y += 6) for (int x = 7; x < 28; x += 6) g.FillRectangle(fill, x, y, 3, 2); g.DrawLine(pen, 8, 23, 24, 23); break;
                case "send": g.DrawPolygon(pen, new PointF[] { new(1, 12), new(31, 1), new(22, 30), new(14, 18) }); g.DrawLine(pen, 14, 18, 29, 3); break;
                case "bell": g.DrawArc(pen, 7, 3, 18, 22, 180, 180); g.DrawLines(pen, new PointF[] { new(7, 13), new(7, 22), new(3, 25), new(29, 25), new(25, 22), new(25, 13) }); g.DrawArc(pen, 12, 24, 8, 6, 0, 180); break;
                case "server": for (int y = 3; y < 28; y += 10) { g.DrawRectangle(pen, 3, y, 26, 7); g.FillEllipse(fill, 6, y + 2, 3, 3); } break;
                case "log": g.DrawRectangle(pen, 6, 2, 20, 28); for (int y = 9; y <= 23; y += 7) g.DrawLine(pen, 11, y, 21, y); break;
                case "min": g.DrawLine(pen, 7, 16, 25, 16); break;
                case "max": g.DrawRectangle(pen, 7, 7, 18, 18); break;
                case "chevron": g.DrawLines(pen, new PointF[] { new(7, 12), new(16, 21), new(25, 12) }); break;
                case "arrow-up-right":
                    g.DrawLine(pen, 5, 27, 26, 6); g.DrawLine(pen, 12, 6, 26, 6);
                    g.DrawLine(pen, 26, 6, 26, 20); break;
            }
            g.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _openMenu?.Dispose(); } catch { }
                _art.Dispose();
                foreach (var preview in _dungeonPreviews) preview?.Dispose();
                foreach (var font in _textFonts.Values) font.Dispose();
                _textFonts.Clear();
                _baseFont.Dispose();
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ReferenceButton : Button
        {
            private readonly ReferenceDashboard _ui;
            private readonly string _icon, _kind;
            private bool _hover, _pressed;
            public ReferenceButton(ReferenceDashboard ui, string icon, string kind)
            {
                _ui = ui; _icon = icon; _kind = kind;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                if (kind == "chrome")
                {
                    // Do not let native Button focus/border visuals masquerade as hover.
                    FlatStyle = FlatStyle.Flat;
                    FlatAppearance.BorderSize = 0;
                    FlatAppearance.MouseOverBackColor = NavBg;
                    FlatAppearance.MouseDownBackColor = NavBg;
                    UseVisualStyleBackColor = false;
                    TabStop = false;
                }
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                float sx = _ui.ScaleX, sy = _ui.ScaleY;
                g.ScaleTransform(sx, sy);
                float w = Width / sx, h = Height / sy;
                if (_kind == "card-link")
                {
                    using var bg = new SolidBrush(_pressed ? AccentSoft : _hover ? CardBg2 : Color.FromArgb(43, 43, 46));
                    using var cardLinkShape = Round(new RectangleF(0.5f, 0.5f, Math.Max(1, w - 1), Math.Max(1, h - 1)), 9);
                    g.FillPath(bg, cardLinkShape);
                    _ui.Icon(g, "arrow-up-right", new(7, 7, w - 14, h - 14), TitleText);
                    return;
                }
                if (_kind == "chrome")
                {
                    // Normal is completely flat. Only actual pointer hover/press
                    // paints a box; Close uses the familiar red hover feedback.
                    bool hot = Enabled && (_hover || _pressed);
                    bool close = _icon == "close";
                    Color surface = !hot ? NavBg : close
                        ? (_pressed ? Color.FromArgb(178, 34, 47) : Color.FromArgb(220, 53, 69))
                        : (_pressed ? Color.FromArgb(43, 43, 49) : Color.FromArgb(51, 51, 59));
                    using (var background = new SolidBrush(surface))
                        g.FillRectangle(background, 0, 0, w, h);
                    if (hot && !close)
                    {
                        using var outline = new Pen(Line, 1);
                        g.DrawRectangle(outline, 0.5f, 0.5f, Math.Max(1, w - 1), Math.Max(1, h - 1));
                    }
                    Color iconColor = !Enabled ? Muted : hot && close ? Color.White : TitleText;
                    _ui.Icon(g, _icon, new((w - 24) / 2, (h - 24) / 2, 24, 24), iconColor);
                    return;
                }
                bool selected = _kind == "choose" && Tag is int index && index == _ui._owner._abyssDungeon.SelectedIndex;
                bool action = _kind == "action";
                bool nav = _kind.StartsWith("nav");
                bool navSelected = nav && (Text == "로그 / 진단"
                    ? _ui._owner._logExpanded
                    : !_ui._owner._logExpanded && Text == _ui._selectedNavigation);
                bool modeSelected = _kind == "mode-tab" && Text == _ui.EffectiveMode;
                bool strong = action || selected || navSelected || modeSelected;
                // Unselected WORKSPACE entries retain visible card backgrounds and borders.
                Color top = navSelected || modeSelected ? AccentSoft : action ? Accent : nav ? NavBg : CardBg;
                Color bottom = top;
                if (action && _ui._owner.AnyRunning) { top = Color.FromArgb(170, 46, 63); bottom = Color.FromArgb(108, 25, 45); }
                if (_pressed) top = action ? Color.FromArgb(29, 93, 209) : (navSelected || modeSelected ? AccentSoft : CardBg2);
                else if (_hover && !navSelected && !modeSelected) top = action ? Color.FromArgb(29, 93, 209) : CardBg2;
                if (!Enabled) { top = bottom = CardBg2; }
                using (var baseFill = new SolidBrush(nav ? NavBg : WindowBg)) g.FillRectangle(baseFill, 0, 0, w, h);
                var r = new RectangleF(1, 1, Math.Max(1, w - 2), Math.Max(1, h - 2));
                using var shape = Round(r, action ? 11 : 7);
                {
                    using var fill = new SolidBrush(top);
                    g.FillPath(fill, shape);
                    using var border = new Pen(navSelected || modeSelected || selected ? _ui._cyan : _ui._line, navSelected || modeSelected || selected ? 1.7f : 1f);
                    g.DrawPath(border, shape);
                    if (navSelected)
                    {
                        using var marker = new SolidBrush(_ui._cyan);
                        g.FillRectangle(marker, 4, 15, 4, Math.Max(4, h - 30));
                    }
                }
                Color fg = !Enabled ? Muted : action ? Color.FromArgb(22, 22, 25) : navSelected || modeSelected ? Blue : TitleText;
                {
                    string caption = Text.TrimStart('●', '○', '▶', '■', '▤', ' ');
                    float font = action ? 19 : nav ? 19 : _kind == "mode-tab" ? 19 : _kind == "log" ? 19 : _kind == "quiet" && w < 150 ? 14 : 18;
                    if (nav || _kind == "mode-tab") { _ui.Icon(g, _icon, new(18, (h - 30) / 2, 30, 30), fg); _ui.TextAt(g, caption, new(67, 0, w - 73, h), font, strong, fg); }
                    else if (_kind == "choose")
                    {
                        using var ring = new Pen(selected ? _ui._cyan : fg, 2);
                        g.DrawEllipse(ring, 14, (h - 20) / 2, 20, 20);
                        if (selected) _ui.Dot(g, 24, h / 2, 6, _ui._cyan);
                        _ui.TextAt(g, selected ? "선택됨" : "선택하기", new(43, 0, w - 78, h), font, selected, fg, StringAlignment.Center);
                    }
                    else if (_kind == "log")
                    {
                        _ui.Icon(g, "log", new(28, (h - 28) / 2, 28, 28), fg);
                        _ui.TextAt(g, caption, new(76, 0, w - 130, h), font, false, fg);
                        _ui.Icon(g, "chevron", new(w - 46, (h - 24) / 2, 24, 24), fg);
                    }
                    else if (action)
                    {
                        float px = Math.Max(13f, MathF.Round(font * Math.Min(sx, sy)));
                        using var labelFont = new Font(_ui._baseFont.FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel);
                        float textWidth = TextRenderer.MeasureText(caption, labelFont,
                            Size.Empty, TextFormatFlags.NoPadding).Width / sx;
                        float startX = Math.Max(12f, (w - (30f + 14f + textWidth)) / 2f);
                        if (_ui._owner.AnyRunning)
                        {
                            using var stop = new SolidBrush(fg);
                            g.FillRectangle(stop, startX + 5, (h - 21) / 2, 21, 21);
                        }
                        else _ui.Icon(g, "play", new(startX, (h - 30) / 2, 30, 30), fg);
                        _ui.TextAt(g, caption, new(startX + 44, 0, textWidth + 12, h),
                            font, true, fg, StringAlignment.Center);
                    }
                    else _ui.TextAt(g, caption, new(5, 0, w - 10, h), font, false, fg, StringAlignment.Center);
                }
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Blue, 1) { DashStyle = DashStyle.Dot };
                    g.DrawRectangle(focus, 5, 5, Math.Max(1, w - 10), Math.Max(1, h - 10));
                }
            }
        }
    }
}
