using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private ProductionDashboard? _productionDashboard;
    private long _productionCurrentQuantity;
    private int _productionTargetQuantity;
    private string _productionDisplayName = "설정에서 선택";
    private string _productionFacilityName = "—";

    private void UpdateProductionDashboardVisibility()
    {
        if (_productionDashboard is null) return;
        string mode = _activeMode ?? SelectedMode;
        bool show = mode is "가공" or "채집";
        _productionDashboard.Visible = show;
        if (show)
        {
            _productionDashboard.ShowMode(mode);
            _productionDashboard.BringToFront();
        }
    }

    private void RefreshProductionDashboard() => _productionDashboard?.RefreshRuntime();

    private sealed class ProductionDashboard : Panel
    {
        private readonly MainForm _owner;
        private readonly List<(Control Control, RectangleF Bounds)> _positions = new();
        private readonly Dictionary<string, Button> _nav = new(StringComparer.Ordinal);
        private readonly TextBox _detailLog = new();
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _resetButton;
        private readonly Button _minButton;
        private readonly Button _maxButton;
        private readonly Button _closeButton;
        private string _mode = "채집";
        private bool? _cliReady;
        private bool? _identityReady;
        private bool? _currencyReady;
        private DateTime _lastConnectionCheck = DateTime.MinValue;
        private bool _connectionCheckRunning;

        private readonly Color _window = Color.FromArgb(8, 17, 31);
        private readonly Color _sidebar = Color.FromArgb(11, 24, 43);
        private readonly Color _card = Color.FromArgb(18, 32, 52);
        private readonly Color _card2 = Color.FromArgb(13, 25, 42);
        private readonly Color _line = Color.FromArgb(35, 58, 86);
        private readonly Color _text = Color.FromArgb(231, 239, 251);
        private readonly Color _muted = Color.FromArgb(166, 187, 216);
        private readonly Color _blue = Color.FromArgb(28, 113, 245);
        private readonly Color _green = Color.FromArgb(51, 221, 131);

        private float ScaleX => Math.Max(1, Width) / 1448f;
        private float ScaleY => Math.Max(1, Height) / 1086f;

        internal ProductionDashboard(MainForm owner)
        {
            _owner = owner;
            BackColor = _window;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            AddNav("홈", new(18, 95, 215, 58), () => HidePage());
            AddNav("낚시", new(18, 163, 215, 58), () => SelectMode(0));
            AddNav("던전", new(18, 231, 215, 58), () => SelectMode(1));
            AddNav("어비스", new(18, 299, 215, 58), () => SelectMode(2));
            AddNav("자동가공", new(18, 395, 215, 58), () => SelectMode(4));
            AddNav("자동채집", new(18, 463, 215, 58), () => SelectMode(5));
            AddNav("설정", new(18, 546, 215, 58), () => HidePage());

            _startButton = AddButton("▶  설정 후 시작  (F9)", new(300, 630, 360, 94), _blue, Color.White,
                () => { if (!_owner.AnyRunning) _owner.StartSelected(); });
            _stopButton = AddButton("■  정지  (F10)", new(680, 630, 265, 94), Color.FromArgb(49, 63, 82), _text,
                _owner.StopSelected);
            _resetButton = AddButton("↻  초기화", new(1260, 223, 128, 42), Color.FromArgb(22, 36, 56), _muted,
                ResetView);

            _minButton = AddButton("—", new(1280, 13, 44, 38), _window, _muted,
                () => _owner.WindowState = FormWindowState.Minimized);
            _maxButton = AddButton("□", new(1330, 13, 44, 38), _window, _muted,
                () =>
                {
                    _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                    _owner.WindowState = _owner.WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal : FormWindowState.Maximized;
                });
            _closeButton = AddButton("×", new(1380, 13, 44, 38), _window, _muted, _owner.Close);

            _detailLog.Multiline = true;
            _detailLog.ReadOnly = true;
            _detailLog.ScrollBars = ScrollBars.Vertical;
            _detailLog.BorderStyle = BorderStyle.None;
            _detailLog.BackColor = Color.FromArgb(6, 15, 27);
            _detailLog.ForeColor = Color.FromArgb(195, 214, 239);
            _detailLog.Font = new Font("Consolas", 9.2f);
            _detailLog.TabStop = false;
            Controls.Add(_detailLog);
            AddPosition(_detailLog, new(300, 848, 1090, 168));

            _owner._log.Line += OnLogLine;
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Y / ScaleY < 64)
                    _owner.DragReferenceWindow();
            };
            DoubleClick += (_, _) =>
            {
                var p = PointToClient(Cursor.Position);
                if (p.Y / ScaleY >= 64) return;
                _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                _owner.WindowState = _owner.WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal : FormWindowState.Maximized;
            };
        }

        internal void ShowMode(string mode)
        {
            _mode = mode == "가공" ? "가공" : "채집";
            foreach (var pair in _nav)
            {
                bool selected = pair.Key == (_mode == "가공" ? "자동가공" : "자동채집");
                pair.Value.BackColor = selected ? Color.FromArgb(26, 77, 157) : _sidebar;
                pair.Value.ForeColor = selected ? Color.White : _muted;
            }
            RefreshRuntime();
            _ = RefreshConnectionsAsync();
        }

        internal void RefreshRuntime()
        {
            if (IsDisposed) return;
            _startButton.Enabled = !_owner.AnyRunning;
            _stopButton.Enabled = _owner.AnyRunning;
            _startButton.BackColor = _startButton.Enabled ? _blue : Color.FromArgb(38, 63, 93);
            _startButton.Text = _owner.AnyRunning ? "▶  실행 중" : "▶  설정 후 시작  (F9)";
            Invalidate();
        }

        private void HidePage()
        {
            Visible = false;
            _owner._referenceDashboard?.BringToFront();
        }

        private void SelectMode(int index)
        {
            if (_owner.AnyRunning || index < 0 || index >= _owner._mode.Items.Count) return;
            _owner._mode.SelectedIndex = index;
            if (index is 4 or 5)
            {
                _mode = index == 4 ? "가공" : "채집";
                ShowMode(_mode);
                Visible = true;
                BringToFront();
            }
            else HidePage();
        }

        private void ResetView()
        {
            if (_owner.AnyRunning) return;
            _owner._productionCurrentQuantity = 0;
            _owner._productionTargetQuantity = 0;
            _owner._productionDisplayName = "설정에서 선택";
            _owner._productionFacilityName = "—";
            _detailLog.Clear();
            RefreshRuntime();
        }

        private async Task RefreshConnectionsAsync()
        {
            if (_connectionCheckRunning || DateTime.Now - _lastConnectionCheck < TimeSpan.FromSeconds(5)) return;
            _connectionCheckRunning = true;
            try
            {
                bool game = WindowTools.EnumerateVisibleWindows().Count > 0;
                if (!game)
                {
                    _cliReady = _identityReady = _currencyReady = false;
                    return;
                }

                var status = await _owner._cli.StatusAsync().ConfigureAwait(true);
                _cliReady = status.Success;
                if (_cliReady == true)
                {
                    var identity = await _owner._cli.GetMyInfoAsync().ConfigureAwait(true);
                    _identityReady = identity.Success;
                    var currencies = await _owner._cli.GetCurrenciesAsync().ConfigureAwait(true);
                    _currencyReady = currencies.Success;
                }
                else _identityReady = _currencyReady = false;
            }
            catch
            {
                _cliReady = _identityReady = _currencyReady = false;
            }
            finally
            {
                _lastConnectionCheck = DateTime.Now;
                _connectionCheckRunning = false;
                if (!IsDisposed) Invalidate();
            }
        }

        private void OnLogLine(string line)
        {
            if (IsDisposed) return;
            if (!(line.Contains("[자동채집]", StringComparison.Ordinal) ||
                  line.Contains("[자동 가공]", StringComparison.Ordinal) ||
                  line.Contains("[CLI]", StringComparison.Ordinal)))
                return;

            void Append()
            {
                if (IsDisposed) return;
                _detailLog.AppendText(line + Environment.NewLine);
                if (_detailLog.TextLength > 22000)
                    _detailLog.Text = _detailLog.Text[^16000..];
                _detailLog.SelectionStart = _detailLog.TextLength;
                _detailLog.ScrollToCaret();
            }

            if (InvokeRequired) BeginInvoke((Action)Append);
            else Append();
        }

        private void AddNav(string text, RectangleF bounds, Action action)
        {
            var button = AddButton(text, bounds, _sidebar, _muted, action);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(28, 0, 0, 0);
            button.Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
            _nav[text] = button;
        }

        private Button AddButton(string text, RectangleF bounds, Color bg, Color fg, Action action)
        {
            var button = new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Blend(bg, Color.White, 0.07f);
            button.Click += (_, _) => action();
            Controls.Add(button);
            AddPosition(button, bounds);
            return button;
        }

        private void AddPosition(Control control, RectangleF bounds)
        {
            _positions.Add((control, bounds));
            control.Bounds = Scale(bounds);
        }

        private Rectangle Scale(RectangleF r) => Rectangle.Round(new RectangleF(
            r.X * ScaleX, r.Y * ScaleY, r.Width * ScaleX, r.Height * ScaleY));

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            foreach (var (control, bounds) in _positions)
            {
                control.Bounds = Scale(bounds);
                float ratio = Math.Min(ScaleX, ScaleY);
                if (control is Button button)
                    button.Font = new Font("맑은 고딕", Math.Max(8f, 10f * ratio / 0.65f), FontStyle.Bold);
                else if (ReferenceEquals(control, _detailLog))
                    control.Font = new Font("Consolas", Math.Max(8f, 9.2f * ratio / 0.65f));
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.ScaleTransform(ScaleX, ScaleY);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 1448, 1086),
                       Color.FromArgb(10, 20, 36), Color.FromArgb(5, 13, 24), 90f))
                g.FillRectangle(bg, 0, 0, 1448, 1086);
            using (var side = new SolidBrush(_sidebar)) g.FillRectangle(side, 0, 64, 252, 1022);
            using (var pen = new Pen(_line))
            {
                g.DrawLine(pen, 0, 64, 1448, 64);
                g.DrawLine(pen, 252, 64, 252, 1086);
            }

            Text(g, "MABI AUTO", new(30, 9, 190, 32), 21, true, Color.White);
            Text(g, UpdateManager.CurrentVersion, new(31, 36, 105, 22), 12, false, _muted);

            bool gameReady = WindowTools.EnumerateVisibleWindows().Count > 0;
            StatusChip(g, new(285, 13, 198, 40), "게임 " + (gameReady ? "연결됨" : "확인 필요"), gameReady);
            StatusChip(g, new(500, 13, 190, 40), "CLI " + (_cliReady == true ? "연결됨" : _cliReady is null ? "확인 중" : "확인 필요"), _cliReady == true);
            StatusChip(g, new(708, 13, 235, 40), _identityReady == true ? "캐릭터 확인됨" : "캐릭터 확인 중", _identityReady == true);

            Text(g, "도움말", new(28, 1015, 110, 25), 13, false, _muted);
            Text(g, "MABI AUTO", new(28, 1042, 120, 22), 11, false, _muted);

            bool altering = _mode == "가공";
            string title = altering ? "자동가공" : "자동 채집";
            string subtitle = altering ? "등록된 가공 작업을 자동으로 진행하고 완료품을 수령합니다."
                                        : "선택한 자원을 자동으로 채집하고 목표 수량에서 정지합니다.";
            Text(g, title, new(300, 88, 600, 48), 30, true, _text);
            Text(g, subtitle, new(300, 135, 760, 30), 14, false, _muted);

            Card(g, new(285, 190, 535, 355));
            Step(g, 1, 305, 212);
            Text(g, altering ? "가공 설정" : "채집 설정", new(345, 207, 280, 38), 20, true, _text);

            string item = _owner._productionDisplayName;
            if (string.IsNullOrWhiteSpace(item) || item == "설정에서 선택")
                item = altering ? "가공 제법을 시작 시 선택" : "채집 품목을 시작 시 선택";
            Field(g, new(468, 274, 315, 54), altering ? "가공 제법" : "채집 품목", item);
            Field(g, new(468, 342, 315, 54), "목표 수량",
                _owner._productionTargetQuantity > 0 ? _owner._productionTargetQuantity.ToString() + " 개" : "시작 시 입력");

            Checkbox(g, 318, 430, "목표 수량 달성 시 자동 정지", true);
            Checkbox(g, 318, 469, "오류 발생 시 자동 정지", true);
            Checkbox(g, 318, 508, altering ? "완료품 자동 수령" : "채집 상태 CLI 검증", true);

            Card(g, new(840, 190, 570, 355));
            Step(g, 2, 860, 212);
            Text(g, "실행 상태", new(900, 207, 260, 38), 20, true, _text);

            string state = string.IsNullOrWhiteSpace(_owner._statusValue.Text) ? "준비" : _owner._statusValue.Text;
            bool running = _owner.AnyRunning && (_owner._activeMode is "가공" or "채집");
            long current = Math.Max(0, _owner._productionCurrentQuantity);
            int target = Math.Max(0, _owner._productionTargetQuantity);
            int percent = target > 0 ? (int)Math.Clamp(current * 100 / target, 0, 100) : 0;
            TimeSpan elapsed = _owner._dungeonStartedAt.HasValue && running
                ? DateTime.Now - _owner._dungeonStartedAt.Value
                : TimeSpan.Zero;

            Text(g, "진행 상태", new(1010, 270, 125, 32), 14, false, _muted);
            Text(g, running ? (altering ? "가공 중" : "채집 중") : state, new(1140, 270, 220, 32), 17, true,
                running ? _green : _text);
            Divider(g, 1010, 308, 1365);

            Text(g, "진행 수량", new(1010, 315, 125, 35), 14, false, _muted);
            Text(g, target > 0 ? $"{current} / {target}" : "— / —", new(1140, 315, 220, 35), 22, true, _text);
            Divider(g, 1010, 356, 1365);

            Text(g, "진행률", new(1010, 364, 125, 34), 14, false, _muted);
            Progress(g, new(1140, 371, 185, 22), percent);
            Text(g, percent + "%", new(1327, 360, 55, 38), 13, false, _muted, StringAlignment.Far);
            Divider(g, 1010, 408, 1365);

            Text(g, "경과 시간", new(1010, 416, 125, 34), 14, false, _muted);
            Text(g, elapsed.ToString(@"hh\:mm\:ss"), new(1140, 416, 220, 34), 16, true, _text);
            Divider(g, 1010, 459, 1365);

            Text(g, altering ? "가공 위치" : "실행 방식", new(1010, 468, 125, 34), 14, false, _muted);
            Text(g, altering
                    ? (_owner._productionFacilityName == "—" ? "시작 시 선택" : _owner._productionFacilityName)
                    : "CLI 상태 확인 + 자동 진행",
                new(1140, 468, 230, 34), 15, true, _text);

            Card(g, new(285, 570, 680, 190));
            Step(g, 3, 305, 590);
            Text(g, "제어", new(345, 585, 220, 40), 20, true, _text);

            Card(g, new(985, 570, 425, 190));
            Step(g, 4, 1005, 590);
            Text(g, "상태 정보", new(1065, 585, 220, 40), 20, true, _text);
            StatusLine(g, 1015, 642, "CLI 연결", _cliReady == true ? "정상" : _cliReady is null ? "확인 중" : "확인 필요", _cliReady == true);
            StatusLine(g, 1015, 676, "캐릭터 확인", _identityReady == true ? "확인됨" : "확인 필요", _identityReady == true);
            StatusLine(g, 1015, 710, "재화 조회", _currencyReady == true ? "정상" : _currencyReady is null ? "확인 중" : "확인 필요", _currencyReady == true);

            Card(g, new(285, 785, 1125, 255));
            Text(g, "상세 로그", new(315, 797, 250, 40), 18, true, _text);
        }

        private void StatusChip(Graphics g, RectangleF r, string caption, bool ok)
        {
            using var path = Round(r, 9);
            using var fill = new SolidBrush(Color.FromArgb(16, 29, 48));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(25, 45, 68));
            g.DrawPath(pen, path);
            Dot(g, r.Right - 20, r.Y + r.Height / 2, 5, ok ? _green : Color.Orange);
            Text(g, caption, new(r.X + 16, r.Y, r.Width - 48, r.Height), 13, true, _text);
        }

        private void StatusLine(Graphics g, float x, float y, string label, string value, bool ok)
        {
            Text(g, label, new(x, y, 145, 30), 14, false, _muted);
            Dot(g, x + 210, y + 15, 5, ok ? _green : Color.Orange);
            Text(g, value, new(x + 230, y, 145, 30), 14, true, _text);
        }

        private void Field(Graphics g, RectangleF r, string label, string value)
        {
            Text(g, label, new(318, r.Y, 135, r.Height), 14, false, _muted);
            using var path = Round(r, 7);
            using var fill = new SolidBrush(Color.FromArgb(13, 25, 42));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(63, 91, 128));
            g.DrawPath(pen, path);
            Text(g, value, new(r.X + 18, r.Y, r.Width - 36, r.Height), 15, true, _text);
        }

        private void Checkbox(Graphics g, float x, float y, string label, bool check)
        {
            var box = new RectangleF(x, y, 23, 23);
            using var path = Round(box, 4);
            using var fill = new SolidBrush(check ? _blue : _card2);
            g.FillPath(fill, path);
            using var pen = new Pen(check ? _blue : _line);
            g.DrawPath(pen, path);
            if (check) Text(g, "✓", new(x, y - 2, 23, 26), 15, true, Color.White, StringAlignment.Center);
            Text(g, label, new(x + 39, y - 4, 405, 32), 14, false, _text);
        }

        private void Step(Graphics g, int number, float x, float y)
        {
            using var fill = new SolidBrush(_blue);
            g.FillEllipse(fill, x, y, 40, 40);
            Text(g, number.ToString(), new(x, y, 40, 40), 16, true, Color.White, StringAlignment.Center);
        }

        private void Card(Graphics g, RectangleF r)
        {
            using var path = Round(r, 10);
            using var fill = new LinearGradientBrush(r, _card, _card2, 35f);
            g.FillPath(fill, path);
            using var pen = new Pen(_line);
            g.DrawPath(pen, path);
        }

        private void Progress(Graphics g, RectangleF r, int percent)
        {
            using (var path = Round(r, 9))
            using (var track = new SolidBrush(Color.FromArgb(51, 72, 103)))
                g.FillPath(track, path);
            if (percent <= 0) return;
            var fillRect = new RectangleF(r.X, r.Y, Math.Max(10, r.Width * percent / 100f), r.Height);
            using var fillPath = Round(fillRect, 9);
            using var fill = new LinearGradientBrush(fillRect, Color.FromArgb(20, 102, 244), Color.FromArgb(34, 136, 255), 0f);
            g.FillPath(fill, fillPath);
        }

        private void Divider(Graphics g, float x1, float y, float x2)
        {
            using var pen = new Pen(Color.FromArgb(27, 47, 70));
            g.DrawLine(pen, x1, y, x2, y);
        }

        private void Dot(Graphics g, float x, float y, float radius, Color color)
        {
            using var glow = new SolidBrush(Color.FromArgb(38, color));
            g.FillEllipse(glow, x - radius - 4, y - radius - 4, (radius + 4) * 2, (radius + 4) * 2);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        }

        private static GraphicsPath Round(RectangleF r, float radius)
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

        private void Text(Graphics g, string? text, RectangleF r, float size, bool bold, Color color,
            StringAlignment align = StringAlignment.Near)
        {
            var points = new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom) };
            using var transform = g.Transform;
            transform.TransformPoints(points);
            var bounds = Rectangle.FromLTRB((int)Math.Round(points[0].X), (int)Math.Round(points[0].Y),
                (int)Math.Round(points[1].X), (int)Math.Round(points[1].Y));
            float scale = Math.Min(Math.Abs(transform.Elements[0]), Math.Abs(transform.Elements[3]));
            float px = Math.Max(11, (float)Math.Round(size * scale));
            using var font = new Font("맑은 고딕", px,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                            TextFormatFlags.PreserveGraphicsClipping;
                flags |= align == StringAlignment.Center ? TextFormatFlags.HorizontalCenter
                    : align == StringAlignment.Far ? TextFormatFlags.Right : TextFormatFlags.Left;
                TextRenderer.DrawText(g, text ?? "", font, bounds, color, flags);
            }
            finally { g.Restore(state); }
        }

        private static Color Blend(Color a, Color b, float amount)
        {
            int r = (int)(a.R + (b.R - a.R) * amount);
            int g = (int)(a.G + (b.G - a.G) * amount);
            int bl = (int)(a.B + (b.B - a.B) * amount);
            return Color.FromArgb(a.A, r, g, bl);
        }
    }
}
