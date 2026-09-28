using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private ReferenceDashboard? _referenceDashboard;

    private void BuildLayout()
    {
        _mode.Items.AddRange(new object[] { "낚시", "던전", "어비스", "페카 심층" });
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
    private sealed class ReferenceDashboard : Panel
    {
        private readonly MainForm _owner;
        private readonly Image _art;
        private readonly Image?[] _dungeonPreviews;
        private readonly List<(Control Control, RectangleF Bounds)> _positions = new();
        private readonly List<ReferenceButton> _gallery = new();
        private readonly ToolTip _tips = new();
        private ContextMenuStrip? _openMenu;
        private readonly Font _baseFont = new("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Color _text = Color.FromArgb(215, 230, 250);
        private readonly Color _cyan = Color.FromArgb(0, 190, 255);
        private readonly Color _green = Color.FromArgb(0, 236, 171);
        private readonly Color _line = Color.FromArgb(0, 79, 126);
        private float ScaleX => Math.Max(1, Width) / 1448f;
        private float ScaleY => Math.Max(1, Height) / 1086f;
        private bool ShouldShowAbyssDungeonPicker =>
            string.Equals(_owner._activeMode ?? _owner._mode.SelectedItem?.ToString(), "어비스", StringComparison.Ordinal);

        public ReferenceDashboard(MainForm owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("FishingAutomation.ReferenceArtwork.png");
            if (stream is null)
            {
                _art = new Bitmap(1448, 1086);
                using var artGraphics = Graphics.FromImage(_art);
                artGraphics.Clear(Color.FromArgb(0, 18, 35));
            }
            else
            {
                using var original = Image.FromStream(stream);
                _art = new Bitmap(original);
            }
            BackColor = Color.FromArgb(0, 16, 31);

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
                BackColor = Color.FromArgb(2, 22, 42),
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
                BackColor = Color.FromArgb(2, 24, 45),
                ForeColor = Color.White,
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
            var autoStopCheckBounds = new RectangleF(1065, 158, 154, 38);
            var autoStopTimeBounds = new RectangleF(1235, 160, 110, 34);
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
            AddButton("홈", new(18, 91, 170, 59), () => { owner._logExpanded = false; owner.ApplyLogVisibility(); }, "home", "nav-selected");
            AddButton("낚시", new(18, 161, 170, 59), () => SelectMode(0), "fish", "nav");
            AddButton("던전", new(18, 231, 170, 59), () => SelectMode(1), "swords", "nav");
            AddButton("어비스", new(18, 301, 170, 59), () => SelectMode(2), "abyss", "nav");
            AddButton("텔레그램", new(18, 371, 170, 59), owner.ShowTelegramSettings, "bell", "nav");
            AddButton("에러 전송", new(18, 441, 170, 59), owner.ShowErrorUploadSettings, "send", "nav");
            AddButton("인식", new(18, 511, 170, 59), owner.ShowVisualRecognitionTest, "image", "nav");
            AddButton("미니 모드", new(18, 991, 170, 46), () => owner.ToggleMini(true), "", "quiet");

            // Mode and dungeon selectors have native keyboard-focusable buttons and menus.
            AddButton("현재 모드 선택", new(418, 249, 39, 63), ShowModeMenu, "chevron", "chrome");
            AddButton("던전 선택", new(680, 249, 38, 63), ShowDungeonMenu, "chevron", "chrome");
            owner._updateButton = AddButton("업데이트 확인", new(1255, 582, 162, 32), () => _ = owner.CheckForUpdatesAsync(true), "", "quiet");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var choose = AddButton("선택하기", new(248 + i * 392, 867, i == 2 ? 369 : 353, 43), () => SelectDungeon(index), "radio", "choose");
                choose.Tag = i;
                _gallery.Add(choose);
                owner._dungeonButtons.Add(choose);
            }
            owner._logToggle = AddButton("로그 보기 (최근 기록)", new(219, 962, 888, 75), () =>
            {
                owner._logExpanded = !owner._logExpanded;
                owner.ApplyLogVisibility();
            }, "log", "log");
            owner._mainActionButton = AddButton("자동 실행 시작", new(1122, 962, 309, 75), () =>
            {
                if (owner.AnyRunning) owner.StopSelected(); else owner.StartSelected();
            }, "play", "action");
            owner._logView = owner.BuildLogPanel();
            owner._logView.Dock = DockStyle.None;
            owner._logView.Visible = false;
            Controls.Add(owner._logView);
            _positions.Add((owner._logView, new(220, 634, 1210, 305)));
            UpdateAbyssDungeonPickerVisibility();
            AccessibleName = "Mabi Auto 대시보드";
        }

        private void SelectMode(int mode)
        {
            _owner.SafeUiAction("현재 모드 선택", () =>
            {
                if (_owner.AnyRunning || _owner._activeMode is not null) return;
                if (mode < 0 || mode >= _owner._mode.Items.Count) return;
                _owner._mode.SelectedIndex = mode;
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
            ShowMenu(new[] { "낚시", "던전", "어비스", "페카 심층" }, SelectMode, 418, 312);
        }

        private void ShowDungeonMenu()
        {
            if (_owner.AnyRunning || _owner._activeMode is not null) return;

            string mode = _owner._mode.SelectedItem?.ToString() ?? "";
            if (string.Equals(mode, "던전", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "현재 위치" }, SelectRegularDungeon, 680, 312);
                return;
            }

            if (string.Equals(mode, "페카 심층", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "페카 심층 1-1", "페카 심층 2-1" }, index =>
                {
                    if (_owner.AnyRunning || _owner._activeMode is not null) return;
                    _owner._selectedPeacaRoute = index == 1 ? "peaca_d2_1" : "peaca_d1_1";
                    _owner.UpdateDashboard();
                }, 680, 312);
                return;
            }

            if (string.Equals(mode, "어비스", StringComparison.Ordinal))
            {
                ShowMenu(new[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" }, SelectDungeon, 680, 312);
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
                    BackColor = Color.FromArgb(3, 28, 49),
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
                Text = text, AccessibleName = text, Cursor = Cursors.Hand, TabStop = true,
                BackColor = Color.FromArgb(3, 29, 51), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
            };
            button.Click += (_, _) => _owner.SafeUiAction(text, action);
            Controls.Add(button);
            _positions.Add((button, bounds));
            return button;
        }

        public void UpdateAbyssDungeonPickerVisibility()
        {
            bool showButtons = ShouldShowAbyssDungeonPicker && !_owner._logExpanded;
            foreach (var button in _gallery) button.Visible = showButtons;
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
            foreach (var (control, r) in _positions)
            {
                control.Bounds = Rectangle.Round(new RectangleF(r.X * ScaleX, r.Y * ScaleY, r.Width * ScaleX, r.Height * ScaleY));
                control.Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            float x = e.X / ScaleX, y = e.Y / ScaleY;
            if (y < 65) { _owner.DragReferenceWindow(); return; }
            if (ShouldShowAbyssDungeonPicker && !_owner._logExpanded && y >= 685 && y <= 921 && x >= 235 && x < 1415)
                SelectDungeon(Math.Clamp((int)((x - 235) / 392), 0, 2));
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
            using var path = Round(r);
            using var gradient = new LinearGradientBrush(r, Color.FromArgb(3, 32, 57), Color.FromArgb(1, 23, 42), 30f);
            g.FillPath(gradient, path);
            if (selected)
            {
                for (int width = 16; width >= 4; width -= 4)
                {
                    using var glow = new Pen(Color.FromArgb(18 + (16 - width) * 2, _cyan), width);
                    g.DrawPath(glow, path);
                }
            }
            using var pen = new Pen(selected ? _cyan : _line, selected ? 3 : 1);
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
            float pixels = Math.Max(size >= 18 ? 13f : 12f, (float)Math.Round(size * scale));
            using var font = new Font(_baseFont.FontFamily, pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
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
            for (int i = 3; i > 0; i--)
            {
                using var glow = new SolidBrush(Color.FromArgb(12, color));
                float r = radius + i * 5;
                g.FillEllipse(glow, x - r, y - r, r * 2, r * 2);
            }
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
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 1448, 1086), Color.FromArgb(0, 20, 40), Color.FromArgb(0, 13, 25), 90f)) g.FillRectangle(bg, 0, 0, 1448, 1086);
            using (var line = new Pen(_line)) { g.DrawLine(line, 0, 65, 1448, 65); g.DrawLine(line, 203, 65, 203, 1086); }
            // Original logo and decorative art only; version and status are live text.
            Artwork(g, new(30, 22, 32, 30), new(27, 21, 39, 37));
            TextAt(g, "Mabi_Auto", new(78, 14, 170, 43), 25, true, Color.White);
            Card(g, new(252, 16, 78, 35));
            TextAt(g, UpdateManager.CurrentVersion, new(252, 18, 78, 31), 14, true, _cyan, StringAlignment.Center);
            Card(g, new(1077, 15, 153, 41));
            bool ready = _owner._sysInputValue.Text == "정상" && _owner._sysTemplateValue.Text == "정상" && _owner._sysOcrValue.Text == "정상";
            Color readyColor = ready ? _green : Color.Gold;
            
            TextAt(g, ready ? "시스템 준비됨" : "확인 필요", new(1087, 20, 137, 29), 15, true, readyColor);

            Card(g, new(219, 81, 1212, 146));
            var saved = g.Save();
            using (var clip = Round(new(221, 83, 1208, 142), 6)) g.SetClip(clip);
            Artwork(g, new(220, 82, 1210, 144), new(220, 82, 1210, 144));
            using (var cover = new SolidBrush(Color.FromArgb(2, 22, 42))) g.FillRectangle(cover, 220, 82, 505, 144);
            using (var fade = new LinearGradientBrush(new Rectangle(724, 82, 190, 144), Color.FromArgb(255, 2, 22, 42), Color.FromArgb(0, 2, 22, 42), 0f)) g.FillRectangle(fade, 724, 82, 190, 144);
            g.Restore(saved);
            Dot(g, 273, 140, 15, _owner.AnyRunning ? _cyan : _green);
            TextAt(g, _owner.AnyRunning ? "매크로 실행 중" : "매크로 준비됨", new(317, 108, 415, 58), 36, true, Color.White);
            TextAt(g, "모든 시스템 상태를 확인하고 자동 진행을 준비합니다.", new(319, 169, 440, 31), 18);
            Card(g, new(1054, 151, 312, 54));

            Info(g, new(219, 241, 248, 83), "crosshair", "현재 모드", _owner._currentModeValue.Text, 239);
            Info(g, new(479, 241, 249, 83), "map", "선택 던전", _owner._currentDungeonValue.Text, 499);
            Info(g, new(740, 241, 202, 83), "list", "현재 단계", _owner._currentStepValue.Text, 757);
            Info(g, new(954, 241, 168, 83), "image", "최근 인식", _owner._recentDetectValue.Text, 973);

            Card(g, new(219, 338, 583, 282));
            Icon(g, "play", new(240, 354, 27, 27), _text);
            TextAt(g, "자동 진행 상태", new(281, 347, 485, 42), 21, true, Color.White);
            TextAt(g, _owner._statusValue.Text, new(240, 394, 540, 32), 22, true, _owner._statusValue.ForeColor);
            using (var track = new SolidBrush(Color.FromArgb(233, 233, 236)))
            using (var path = Round(new(240, 431, 542, 16), 4)) g.FillPath(track, path);
            if (_owner._progress.Value > 0)
            {
                using var fill = new SolidBrush(_cyan);
                using var path = Round(new(240, 431, Math.Max(8, 542 * _owner._progress.Value / 100f), 16), 4);
                g.FillPath(fill, path);
            }
            TextAt(g, _owner._stageTime.Text, new(241, 451, 540, 31), 14, false, _text, StringAlignment.Far);
            using (var logBg = new SolidBrush(Color.FromArgb(0, 18, 34)))
            using (var path = Round(new(239, 484, 543, 120), 5)) g.FillPath(logBg, path);
            string preview = string.IsNullOrWhiteSpace(_owner._progressLogPreview.Text) ? "최근 진행 로그가 여기에 표시됩니다." : _owner._progressLogPreview.Text;
            TextAt(g, preview, new(259, 498, 498, 94), 15);

            Card(g, new(814, 338, 309, 282));
            Icon(g, "stats", new(835, 358, 30, 31), _cyan);
            TextAt(g, "실행 통계", new(878, 349, 214, 45), 21, true, Color.White);
            Card(g, new(830, 403, 279, 203));
            Stat(g, 405, "check", "성공", _owner._successValue.Text, _green);
            Stat(g, 455, "close", "실패", _owner._failureValue.Text, Color.FromArgb(255, 65, 64));
            Stat(g, 505, "refresh", "복구", _owner._timeoutValue.Text, _cyan);
            Stat(g, 555, "clock", "총 실행 시간", _owner._elapsedValue.Text, _cyan);

            Card(g, new(1134, 241, 297, 379));
            Icon(g, "pulse", new(1157, 259, 36, 37), _cyan);
            TextAt(g, "시스템 상태", new(1209, 257, 201, 42), 22, true, Color.White);
            Card(g, new(1148, 308, 270, 270));
            var status = new (string Icon, string Caption, Label Value)[]
            {
                ("game", "게임 창 인식", _owner._sysGameValue), ("image", "이미지 템플릿", _owner._sysTemplateValue),
                ("ocr", "OCR 기능", _owner._sysOcrValue), ("keyboard", "키보드 입력", _owner._sysInputValue),
                ("send", "와치독", _owner._sysWatchdogValue), ("bell", "텔레그램 알림", _owner._sysTelegramValue),
                ("server", "업데이트 서버", _owner._sysUpdateValue)
            };
            for (int i = 0; i < status.Length; i++)
            {
                float y = 313 + i * 38;
                // Reserve distinct columns for the full caption, indicator, and full status.
                TextAt(g, status[i].Caption, new(1159, y, 132, 37), 17);
                Color c = status[i].Value.ForeColor;
                Dot(g, 1301, y + 19, 4, c);
                TextAt(g, status[i].Value.Text, new(1315, y, 95, 37), 14, true, c, StringAlignment.Far);
            }
            TextAt(g, _owner._updateStatusValue.Text, new(1153, 582, 98, 34), 14, false, _owner._updateStatusValue.ForeColor);

            if (ShouldShowAbyssDungeonPicker)
            {
                Card(g, new(219, 634, 1212, 305));
                Icon(g, "game", new(240, 650, 29, 26), _text);
                TextAt(g, "어비스 던전 선택", new(286, 644, 1089, 37), 21, true, Color.White);
                string[] names = { "허상의 정박지", "광기의 동굴", "흩어진 물길" };
                string[] desc = { "고요 속에 가라앉은 진실", "뒤틀린 광기의 울림", "흩어진 흐름이 이어지는 곳" };
                for (int i = 0; i < 3; i++)
                {
                    int x = 235 + i * 392;
                    int tileWidth = i == 2 ? 396 : 380;
                    Card(g, new(x, 685, tileWidth, 236), i == _owner._abyssDungeon.SelectedIndex);
                    var previewBounds = new RectangleF(x + 6, 692, tileWidth - 12, 105);
                    if (_dungeonPreviews[i] is Image dungeonPreview)
                        g.DrawImage(dungeonPreview, previewBounds);
                    else
                    {
                        using var missing = new SolidBrush(Color.FromArgb(2, 22, 42));
                        g.FillRectangle(missing, previewBounds);
                        TextAt(g, "미리보기 이미지 확인 필요", previewBounds, 14, false, Color.Orange, StringAlignment.Center);
                    }
                    TextAt(g, names[i], new(x + 12, 801, tileWidth - 24, 33), 20, true, Color.White, StringAlignment.Center);
                    TextAt(g, desc[i], new(x + 12, 833, tileWidth - 24, 28), 17, false, _text, StringAlignment.Center);
                }
            }
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
                TextAt(g, value, new(r.X + 11, r.Y + 36, r.Width - 22, 44), 13, true, Color.White, StringAlignment.Center, multiline: true);
                return;
            }

            Icon(g, icon, new(iconX, r.Y + 24, 35, 35), _cyan);
            float left = iconX + 55;
            float reserved = caption is "현재 모드" or "선택 던전" ? 42 : 10;
            TextAt(g, caption, new(left, r.Y + 12, r.Right - left - reserved, 24), 16);
            TextAt(g, value, new(left, r.Y + 32, r.Right - left - reserved, 50), 18, true, Color.White);
        }

        private void Stat(Graphics g, float y, string icon, string caption, string value, Color c)
        {
            Icon(g, icon, new(844, y + 10, 26, 26), c);
            TextAt(g, caption, new(884, y, 124, 46), 16);
            TextAt(g, string.IsNullOrWhiteSpace(value) ? "0" : value, new(1007, y, 83, 46), 20, true, Color.White, StringAlignment.Far);
            if (y < 550) { using var pen = new Pen(Color.FromArgb(11, 53, 81)); g.DrawLine(pen, 844, y + 48, 1096, y + 48); }
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
                    using (var eye = new SolidBrush(Color.FromArgb(0, 22, 42))) g.FillEllipse(eye, 23, 11, 3, 3); break;
                case "swords":
                    g.DrawLine(pen, 5, 3, 27, 28); g.DrawLine(pen, 27, 3, 5, 28); g.DrawLine(pen, 1, 19, 12, 30); g.DrawLine(pen, 20, 30, 31, 19); break;
                case "abyss":
                    g.FillPolygon(fill, new PointF[] { new(16, 0), new(30, 16), new(16, 32), new(2, 16) });
                    using (var hole = new SolidBrush(Color.FromArgb(0, 22, 42))) g.FillEllipse(hole, 10, 10, 12, 12); break;
                case "leaf": g.DrawArc(pen, 4, 3, 25, 25, 90, 270); g.DrawLine(pen, 2, 30, 28, 3); g.DrawLine(pen, 7, 27, 29, 3); break;
                case "settings":
                    g.DrawEllipse(pen, 8, 8, 16, 16); g.DrawEllipse(pen, 13, 13, 6, 6);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 16 + 10 * (float)Math.Cos(a), 16 + 10 * (float)Math.Sin(a), 16 + 15 * (float)Math.Cos(a), 16 + 15 * (float)Math.Sin(a)); } break;
                case "crosshair": g.DrawEllipse(pen, 5, 5, 22, 22); g.DrawLine(pen, 16, 0, 16, 10); g.DrawLine(pen, 16, 22, 16, 32); g.DrawLine(pen, 0, 16, 10, 16); g.DrawLine(pen, 22, 16, 32, 16); break;
                case "map": g.FillPolygon(fill, new PointF[] { new(2, 4), new(10, 1), new(10, 28), new(2, 31) }); g.FillPolygon(fill, new PointF[] { new(13, 2), new(20, 5), new(20, 31), new(13, 28) }); g.FillPolygon(fill, new PointF[] { new(23, 5), new(30, 1), new(30, 27), new(23, 31) }); break;
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
                bool selected = _kind == "choose" && Tag is int index && index == _ui._owner._abyssDungeon.SelectedIndex;
                bool action = _kind == "action";
                bool nav = _kind.StartsWith("nav");
                bool strong = action || selected || _kind == "nav-selected";
                bool bare = _kind is "chrome" or "nav";
                Color top = strong ? Color.FromArgb(0, 136, 255) : Color.FromArgb(3, 31, 54);
                Color bottom = strong ? Color.FromArgb(0, 68, 207) : Color.FromArgb(1, 20, 37);
                if (action && _ui._owner.AnyRunning) { top = Color.FromArgb(170, 46, 63); bottom = Color.FromArgb(108, 25, 45); }
                if (_pressed) top = bottom;
                else if (_hover) top = Color.FromArgb(15, 88, 142);
                if (!Enabled) { top = bottom = Color.FromArgb(1, 19, 34); }
                using (var baseFill = new SolidBrush(Color.FromArgb(0, 18, 34))) g.FillRectangle(baseFill, 0, 0, w, h);
                var r = new RectangleF(1, 1, Math.Max(1, w - 2), Math.Max(1, h - 2));
                using var shape = Round(r, action ? 11 : 7);
                if (!bare || _hover)
                {
                    using var fill = new LinearGradientBrush(r, top, bottom, 90f);
                    g.FillPath(fill, shape);
                    using var border = new Pen(strong ? _ui._cyan : _ui._line, strong ? 1.7f : 1f);
                    g.DrawPath(border, shape);
                }
                Color fg = Enabled ? Color.FromArgb(206, 225, 255) : Color.FromArgb(98, 125, 150);
                if (_kind == "chrome")
                {
                    _ui.Icon(g, _icon, new((w - 24) / 2, (h - 24) / 2, 24, 24), fg);
                }
                else
                {
                    string caption = Text.TrimStart('●', '○', '▶', '■', '▤', ' ');
                    float font = action ? 22 : nav ? 20 : _kind == "log" ? 20 : _kind == "quiet" && w < 150 ? 14 : 18;
                    if (nav) { _ui.Icon(g, _icon, new(18, (h - 30) / 2, 30, 30), fg); _ui.TextAt(g, caption, new(67, 0, w - 73, h), font, strong, fg); }
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
                        if (_ui._owner.AnyRunning) { using var stop = new SolidBrush(fg); g.FillRectangle(stop, 71, (h - 22) / 2, 22, 22); }
                        else _ui.Icon(g, "play", new(70, (h - 30) / 2, 30, 30), fg);
                        _ui.TextAt(g, caption, new(112, 0, w - 120, h), font, true, Color.White);
                    }
                    else _ui.TextAt(g, caption, new(5, 0, w - 10, h), font, false, fg, StringAlignment.Center);
                }
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Color.White, 1) { DashStyle = DashStyle.Dot };
                    g.DrawRectangle(focus, 5, 5, Math.Max(1, w - 10), Math.Max(1, h - 10));
                }
            }
        }
    }
}

