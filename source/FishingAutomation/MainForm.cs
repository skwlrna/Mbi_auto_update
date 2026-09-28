using System.Text.Json;
using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm : Form
{
    private const int HOTKEY_TEST = 9000;
    private const int HOTKEY_START = 9001;
    private const int HOTKEY_STOP = 9002;

    private readonly FishingBot _fishingBot;
    private readonly AppLog _log;
    private readonly TelegramNotifier _notifier;
    private readonly RuntimeErrorUploader _errorUploader;
    private readonly WatchdogClient _watchdog;
    private DateTime _lastFishingProgressAt = DateTime.Now;
    private bool _fishingStallAlerted;
    private string _lastFishingStatus = "준비";
    private DateTime? _exitProcedureDeadline;
    private bool _exitProcedureAlerted;
    private int _networkRetryCount;

    private readonly ComboBox _mode = new();
    private readonly ComboBox _dungeonDestination = new();
    private readonly ComboBox _abyssDungeon = new();
    private readonly Label _abyssDungeonLabel = new();
    private readonly Label _statusValue = new();
    private readonly Label _inputValue = new();
    private readonly Label _elapsedValue = new();
    private readonly Label _roundValue = new();
    private readonly Label _successValue = new();
    private readonly Label _rateValue = new();
    private readonly TextBox _logBox = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };
    private bool _autoStopEnabled;
    private TimeSpan _autoStopTime = new(2, 30, 0);
    private DateTime? _autoStopLastTriggeredDate;
    private DateTime _autoStopLastCheck = DateTime.Now;
    private string _autoStopSettingsPath = "";

    private DateTime? _fishingStartedAt;
    private int _fishingRounds;
    private int _fishingSuccesses;

    private CancellationTokenSource? _dungeonCts;
    private Task? _dungeonTask;
    private DateTime? _dungeonStartedAt;
    private int _dungeonCycles;
    private int _dungeonCompleted;
    private string _dungeonInputName = "준비 중";

    private string? _activeMode;
    private bool _starting;
    private bool _cancelStart;

    private static readonly Color WindowBg = Color.FromArgb(4, 22, 38);
    private static readonly Color PanelBg = Color.FromArgb(9, 34, 58);
    private static readonly Color Border = Color.FromArgb(20, 66, 103);
    private static readonly Color TitleText = Color.FromArgb(235, 244, 252);
    private static readonly Color Blue = Color.FromArgb(20, 132, 255);
    private static readonly Color Green = Color.FromArgb(56, 232, 145);

    public MainForm()
    {
        string baseDir = AppContext.BaseDirectory;
        AutomationConfig cfg = AutomationConfig.Load(Path.Combine(baseDir, "config.json"));
        string runtimeLogPath = Path.Combine(baseDir, cfg.LogFile);
        _log = new AppLog(runtimeLogPath, cfg.LogMaxBytes);
        _notifier = new TelegramNotifier(Path.Combine(baseDir, "notification.json"), _log);
        _errorUploader = new RuntimeErrorUploader(_log, runtimeLogPath);
        _watchdog = new WatchdogClient(baseDir);
        _fishingBot = new FishingBot(cfg, _log);
        LoadAutoStopSettings();

        Text = "Mabi_Auto";
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1448, 1086);
        float uiScale = 0.65f * Math.Min(1f, Math.Min((area.Width - 32) / 1448f, (area.Height - 32) / 1086f));
        ClientSize = new Size((int)(1448 * uiScale), (int)(1086 * uiScale));
        MinimumSize = new Size(Math.Min(940, area.Width), Math.Min(700, area.Height));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WindowBg;
        Font = new Font("맑은 고딕", 8.5f);
        FormBorderStyle = FormBorderStyle.None;
        MaximizedBounds = area;
        DoubleBuffered = true;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.None; // The reference dashboard scales its controls and fonts together.
        Padding = new Padding(0);

        BuildLayout();

        Shown += async (_, _) =>
        {
            // 창이 실제로 표시된 뒤 잠시 정상 실행되는 것을 확인하고 업데이트 성공을 기록한다.
            await Task.Delay(1200);
            if (IsDisposed) return;
            UpdateManager.MarkStartupHealthy();
            _notifier.StartRemoteControl(HandleTelegramCommandAsync);
            await RunStartupDiagnosticsAsync();
            await CheckForUpdatesAsync(false);
        };

        _fishingBot.StatusChanged += s => Ui(() =>
        {
            _lastFishingStatus = s;
            if (SelectedMode == "낚시" && (_activeMode is null || _activeMode == "낚시"))
                SetStatus(_fishingBot.IsRunning ? s : "준비 완료", _fishingBot.IsRunning ? Blue : Green);
        });

        _log.Line += s => Ui(() =>
        {
            _logBox.AppendText(s + Environment.NewLine);
            if (_logBox.TextLength > 35000) _logBox.Text = _logBox.Text[^22000..];
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();

            TrackAlertHealthFromLog(s);

            if (!s.Contains("[던전]"))
            {
                if (s.Contains("낚싯대 아이콘") && s.Contains("-> Space") && !s.Contains("복귀"))
                    _fishingRounds++;
                if (s.Contains("챔질:") && s.Contains("Space"))
                    _fishingSuccesses++;
            }
            UpdateStats();
        });

        _mode.SelectedIndexChanged += (_, _) =>
        {
            UpdateAbyssSelectorVisibility();
            if (_activeMode is null)
            {
                RefreshModeStatus();
                UpdateStats();
            }
        };

        _dungeonDestination.SelectedIndexChanged += (_, _) =>
        {
            if (_activeMode is null && SelectedMode == "던전")
            {
                _log.Write($"[던전] 선택 던전: {SelectedDungeonDestination}");
                RefreshModeStatus();
                UpdateDashboard();
            }
        };

        _abyssDungeon.SelectedIndexChanged += (_, _) =>
        {
            if (_activeMode is null && SelectedMode == "어비스")
            {
                _log.Write($"[어비스] 선택 던전: {SelectedAbyssDungeon}");
                RefreshModeStatus();
            }
        };

        _uiTimer.Tick += (_, _) => { _watchdog.Touch(); CheckAlertHealth(); CheckAutoStop(); UpdateStats(); };
        _uiTimer.Start();

        FormClosed += (_, _) =>
        {
            _uiTimer.Stop();
            StopAll();
            _watchdog.Dispose();
            _notifier.Dispose();
            _fishingBot.Dispose();
        };

        RefreshModeStatus();
        UpdateStats();
    }

    private string SelectedMode => _mode.SelectedItem?.ToString() ?? "낚시";
    private string SelectedDungeonDestination => "현재 위치";
    private string _selectedPeacaRoute = "peaca_d1_1";
    private string SelectedPeacaDestination => _selectedPeacaRoute == "peaca_d2_1" ? "페카 심층 2-1" : "페카 심층 1-1";
    private string SelectedAbyssDungeon => _abyssDungeon.SelectedItem?.ToString() ?? "허상의 정박지";
    private string SelectedAbyssTargetId => SelectedAbyssDungeon switch
    {
        "광기의 동굴" => "abyss_dungeon_madness_cave",
        "흩어진 물길" => "abyss_dungeon_scattered_waterway",
        _ => "abyss_dungeon_hallucination_anchorage"
    };
    private bool DungeonRunning => _dungeonTask is { IsCompleted: false };
    private bool AnyRunning => _starting || _fishingBot.IsRunning || DungeonRunning;

    private void UpdateAbyssSelectorVisibility()
    {
        bool show = SelectedMode == "어비스";
        _abyssDungeonLabel.Visible = show;
        _abyssDungeon.Visible = show;
        _abyssDungeon.Enabled = show && _activeMode is null;
        _referenceDashboard?.UpdateAbyssDungeonPickerVisibility();
    }

    private Control BuildButtonsPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = WindowBg, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.Controls.Add(CreateMainButton("시작 (F9)", Blue, Color.White, StartSelected, new Padding(0, 0, 6, 0)), 0, 0);
        layout.Controls.Add(CreateMainButton("정지 (F10)", PanelBg, TitleText, StopSelected, new Padding(3, 0, 3, 0)), 1, 0);
        layout.Controls.Add(CreateMainButton("테스트 / 새로고침 (F8)", PanelBg, TitleText, TestOrRefresh, new Padding(6, 0, 0, 0)), 2, 0);
        return layout;
    }

    private Control BuildLogPanel()
    {
        var panel = CreateCard();
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = PanelBg };
        header.Controls.Add(new Label { Text = "실시간 로그", Dock = DockStyle.Left, Width = 140, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = TitleText });
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Dock = DockStyle.Fill;
        _logBox.Font = new Font("Consolas", 8.6f);
        _logBox.BackColor = PanelBg;
        _logBox.ForeColor = TitleText;
        _logBox.BorderStyle = BorderStyle.None;
        var body = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg, Padding = new Padding(12, 4, 12, 10) };
        body.Controls.Add(_logBox);
        panel.Controls.Add(body);
        panel.Controls.Add(header);
        return panel;
    }

    private Panel CreateCard()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg };
        panel.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle, Border, ButtonBorderStyle.Solid);
        return panel;
    }

    private Control CreateMainButton(string text, Color bg, Color fg, Action onClick, Padding margin)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = margin, FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand, UseCompatibleTextRendering = true };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control CreateStatCell(string caption, Label valueLabel, bool divider)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg, Margin = new Padding(0) };
        panel.Paint += (_, e) => { if (divider) e.Graphics.DrawLine(new Pen(Border), panel.Width - 1, 8, panel.Width - 1, panel.Height - 8); };
        var cap = new Label { Text = caption, Font = new Font("Segoe UI", 9.8f, FontStyle.Bold), ForeColor = TitleText, Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleCenter };
        valueLabel.Text = "0";
        valueLabel.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        valueLabel.ForeColor = TitleText;
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleCenter;
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(cap);
        return panel;
    }

    private void StartSelected()
    {
        if (AnyRunning)
        {
            SetStatus($"{_activeMode} 실행 중", Blue);
            _log.Write($"이미 {_activeMode} 모드가 실행 중입니다. 먼저 F10으로 정지하세요.");
            return;
        }

        if (SelectedMode == "낚시") StartFishing();
        else if (SelectedMode == "던전") _ = StartScenarioAsync("던전", "dungeon");
        else if (SelectedMode == "페카 심층") _ = StartScenarioAsync("페카 심층", "dungeon");
        else _ = StartScenarioAsync("어비스", "abyss");
    }

    private void StartFishing()
    {
        if (!_fishingBot.InputReady)
        {
            SetStatus("입력 준비 필요", Color.Firebrick);
            _log.Write("낚시 시작 실패: Interception 입력 준비가 필요합니다.");
            return;
        }

        _activeMode = "낚시";
        _mode.Enabled = false;
        _fishingStartedAt = DateTime.Now;
        _fishingStoppedAt = null;
        _fishingRounds = _fishingSuccesses = 0;
        _lastFishingProgressAt = DateTime.Now;
        _fishingStallAlerted = false;
        _networkRetryCount = 0;
        _log.Write("[낚시] 시작(F9)");
        _fishingBot.Start();
        _inputValue.Text = _fishingBot.InputName;
        SetStatus("낚시 실행 중", Blue);
        UpdateStats();
    }

    private async Task StartScenarioAsync(string modeName, string folderName)
    {
        _starting = true;
        _cancelStart = false;
        string baseDir = Path.Combine(AppContext.BaseDirectory, folderName);
        try
        {
            var window = WindowTools.EnumerateVisibleWindows().FirstOrDefault();
            if (window is null)
            {
                SetStatus("게임 창 없음", Color.Firebrick);
                _log.Write($"[{modeName}] 마비노기 모바일 창을 찾지 못했습니다.");
                return;
            }

            var settings = LoadJson<AppSettings>(Path.Combine(baseDir, "config", "appsettings.json"));
            var targets = LoadJson<List<TargetDefinition>>(Path.Combine(baseDir, "config", "targets.json"));
            var scenario = LoadJson<ScenarioDefinition>(Path.Combine(baseDir, "config", "scenario.json"));

            if (modeName == "어비스")
            {
                foreach (var step in scenario.Steps)
                {
                    if (step.Target.Equals("abyss_selected_dungeon", StringComparison.OrdinalIgnoreCase))
                        step.Target = SelectedAbyssTargetId;
                }
                _log.Write($"[어비스] 무한 반복 · 선택 던전={SelectedAbyssDungeon}");
            }

            // v59: Dungeon/Abyss always use the canonical client geometry.
            WindowTools.EnsureClientSizeAndTopRight(window.Handle, settings.ClientWidth, settings.ClientHeight);
            await Task.Delay(500);

            if (_cancelStart || IsDisposed) return;
            _activeMode = modeName;
            _mode.Enabled = false;
            _abyssDungeon.Enabled = false;
            _dungeonCycles = 0;
            _dungeonCompleted = 0;
            _dungeonStartedAt = DateTime.Now;
            _dungeonStoppedAt = null;
            _timeoutExits = 0;
            _exitProcedureDeadline = null;
            _exitProcedureAlerted = false;
            _networkRetryCount = 0;
            _runError = null;
            _stageStartedAt = _restartAt = null;
            _dungeonCts = new CancellationTokenSource();

            IScenarioRunner engine = modeName == "페카 심층"
                ? new PeacaRouteEngine(window.Handle, settings, targets, baseDir, _selectedPeacaRoute)
                : new ScenarioEngine(window.Handle, settings, scenario, targets, baseDir);
            if (modeName == "던전")
                _log.Write("[던전] 현재 위치에서 시작");
            _dungeonInputName = engine.InputMode;
            _inputValue.Text = _dungeonInputName;
            SetStatus($"{modeName} 실행 중", Blue);
            _log.Write($"[{modeName}] 시작(F9) · 창={window.Title} · 입력={engine.InputMode}");

            engine.Log += text =>
            {
                Ui(() => ProcessScenarioLog(modeName, text));
            };

            _dungeonTask = Task.Run(async () =>
            {
                using (engine)
                {
                    try
                    {
                        await engine.RunAsync(_dungeonCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Ui(() => _log.Write($"[{modeName}] 정지되었습니다."));
                    }
                    catch (Exception ex)
                    {
                        Ui(() => { _runError = ex.Message; _logExpanded = true; ApplyLogVisibility(); _log.Write($"[{modeName}] 오류: " + ex.Message); _ = SendRuntimeAlertAsync($"{modeName} 매크로 오류", ex.Message, true); });
                    }
                    finally
                    {
                        Ui(() =>
                        {
                            _dungeonStoppedAt = DateTime.Now;
                            _stageStartedAt = _restartAt = null;
                            _activeMode = null;
                            _mode.Enabled = true;
                            UpdateAbyssSelectorVisibility();
                            SetStatus("준비 완료", Green);
                            RefreshModeStatus();
                            if (_runError is not null) SetStatus("오류: " + _runError, Color.Salmon);
                            UpdateStats();
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _activeMode = null;
            _mode.Enabled = true;
            UpdateAbyssSelectorVisibility();
            _logExpanded = true;
            ApplyLogVisibility();
            SetStatus($"{modeName} 시작 실패", Color.Salmon);
            _log.Write($"[{modeName}] 시작 실패: " + ex.Message);
            _ = SendRuntimeAlertAsync($"{modeName} 시작 실패", ex.Message, true);
        }
        finally { _starting = false; }
    }

    private void ProcessScenarioLog(string modeName, string text)
    {
        TrackProgress(modeName, text);
        if (text.Contains("판 시작")) _dungeonCycles++;
        if (text.Contains("판 완료")) _dungeonCompleted++;
        if (text.StartsWith("입력 모드:", StringComparison.OrdinalIgnoreCase))
        {
            _dungeonInputName = text.Substring("입력 모드:".Length).Trim();
            _inputValue.Text = _dungeonInputName;
        }
        _log.Write($"[{modeName}] " + text);
        if (text.StartsWith("[자동복구] 1/", StringComparison.OrdinalIgnoreCase) && text.Contains("단계 시간초과"))
            _ = SendRuntimeAlertAsync($"{modeName} 단계 자동복구 시작", text, true);
        UpdateStats();
    }

    private void LoadAutoStopSettings()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MabiAuto");
            _autoStopSettingsPath = Path.Combine(dir, "auto-stop.json");
            if (!File.Exists(_autoStopSettingsPath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(_autoStopSettingsPath));
            var rootNode = doc.RootElement;
            if (rootNode.TryGetProperty("Enabled", out var enabled) &&
                (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
                _autoStopEnabled = enabled.GetBoolean();

            if (rootNode.TryGetProperty("Time", out var timeNode))
            {
                string? value = timeNode.GetString();
                string[] parts = (value ?? "").Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int hour) &&
                    int.TryParse(parts[1], out int minute) &&
                    hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
                    _autoStopTime = new TimeSpan(hour, minute, 0);
            }
        }
        catch { }
    }

    private void SaveAutoStopSettings()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_autoStopSettingsPath))
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MabiAuto");
                _autoStopSettingsPath = Path.Combine(dir, "auto-stop.json");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_autoStopSettingsPath)!);
            var data = new
            {
                Enabled = _autoStopEnabled,
                Time = $"{(int)_autoStopTime.TotalHours:00}:{_autoStopTime.Minutes:00}"
            };
            File.WriteAllText(_autoStopSettingsPath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void SetAutoStopEnabled(bool enabled)
    {
        _autoStopEnabled = enabled;
        SaveAutoStopSettings();
    }

    private void SetAutoStopTime(DateTime value)
    {
        _autoStopTime = new TimeSpan(value.Hour, value.Minute, 0);
        SaveAutoStopSettings();
    }

    private void CheckAutoStop()
    {
        DateTime previous = _autoStopLastCheck;
        _autoStopLastCheck = DateTime.Now;
        DateTime now = _autoStopLastCheck;
        if (!_autoStopEnabled || !AnyRunning) return;

        DateTime scheduled = now.Date + _autoStopTime;
        bool crossedSchedule = previous < scheduled && now >= scheduled;
        bool sameMinute = now.Hour == _autoStopTime.Hours && now.Minute == _autoStopTime.Minutes;
        if (!crossedSchedule && !sameMinute) return;
        if (_autoStopLastTriggeredDate?.Date == now.Date) return;

        _autoStopLastTriggeredDate = now.Date;
        _log.Write($"[자동 정지] 예약 시간 {_autoStopTime:hh\\:mm} 도달 -> F10과 동일한 안전 정지");
        StopSelected();
    }

    private void StopSelected()
    {
        if (_starting) _cancelStart = true;
        if (_fishingBot.IsRunning)
        {
            _fishingStoppedAt = DateTime.Now;
            _fishingBot.Stop();
            _log.Write("[낚시] 정지(F10)");
            _activeMode = null;
            _mode.Enabled = true;
            UpdateAbyssSelectorVisibility();
            RefreshModeStatus();
        }

        if (DungeonRunning && _dungeonCts is not null && !_dungeonCts.IsCancellationRequested)
        {
            _log.Write($"[{_activeMode ?? "던전"}] 정지 요청(F10)");
            _dungeonCts.Cancel();
        }
        UpdateStats();
    }

    private void StopAll()
    {
        _cancelStart = true;
        if (_fishingBot.IsRunning) _fishingBot.Stop();
        if (_dungeonCts is not null && !_dungeonCts.IsCancellationRequested) _dungeonCts.Cancel();
    }

    private void TestOrRefresh()
    {
        if (AnyRunning)
        {
            _log.Write("실행 중에는 F8 테스트/새로고침을 사용하지 않습니다.");
            return;
        }

        if (SelectedMode == "낚시")
        {
            _log.Write("[낚시] Space 입력 테스트(F8)");
            _fishingBot.TestSpace();
        }
        else
        {
            var windows = WindowTools.EnumerateVisibleWindows();
            _log.Write($"[{SelectedMode}] 마비노기 모바일 창 새로고침: {windows.Count}개 발견");
            SetStatus(windows.Count > 0 ? $"{SelectedMode} 준비 완료" : "게임 창 없음", windows.Count > 0 ? Green : Color.Firebrick);
        }
    }

    private void RefreshModeStatus()
    {
        if (_activeMode is not null) return;
        if (SelectedMode == "낚시")
        {
            _inputValue.Text = _fishingBot.InputName;
            SetStatus(_fishingBot.InputReady ? "낚시 준비 완료" : "입력 준비 필요", _fishingBot.InputReady ? Green : Color.Firebrick);
        }
        else
        {
            _inputValue.Text = _dungeonInputName;
            int count = WindowTools.EnumerateVisibleWindows().Count;
            string readyText = SelectedMode == "어비스"
                ? $"어비스 준비 완료 · {SelectedAbyssDungeon}"
                : $"{SelectedMode} 준비 완료";
            SetStatus(count > 0 ? readyText : "게임 창 확인 필요", count > 0 ? Green : Color.DarkOrange);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusValue.Text = text;
        _statusValue.ForeColor = color;
        _miniStatus.Text = text;
    }

    private void UpdateStats()
    {
        UpdateDashboard();
        string mode = _activeMode ?? SelectedMode;
        if (mode == "던전" || mode == "어비스" || mode == "페카 심층")
        {
            var elapsed = _dungeonStartedAt.HasValue ? (_dungeonStoppedAt ?? DateTime.Now) - _dungeonStartedAt.Value : TimeSpan.Zero;
            int failures = Math.Max(0, _dungeonCycles - _dungeonCompleted - (DungeonRunning ? 1 : 0));
            _elapsedValue.Text = elapsed.ToString(@"hh\:mm\:ss");
            _roundValue.Text = _dungeonCycles.ToString();
            _successValue.Text = _dungeonCompleted.ToString();
            _failureValue.Text = Math.Max(failures, _timeoutExits).ToString();
            _rateValue.Text = _dungeonCycles > 0 ? $"{Math.Round(_dungeonCompleted * 100.0 / _dungeonCycles):0}%" : "0%";
            _averageValue.Text = _dungeonCompleted > 0 ? TimeSpan.FromSeconds(elapsed.TotalSeconds / _dungeonCompleted).ToString(@"mm\:ss") : "—";
            _startTimeValue.Text = _dungeonStartedAt?.ToString("HH:mm:ss") ?? "—";
        }
        else
        {
            var elapsed = _fishingStartedAt.HasValue ? (_fishingStoppedAt ?? DateTime.Now) - _fishingStartedAt.Value : TimeSpan.Zero;
            int failures = Math.Max(0, _fishingRounds - _fishingSuccesses);
            _elapsedValue.Text = elapsed.ToString(@"hh\:mm\:ss");
            _roundValue.Text = _fishingRounds.ToString();
            _successValue.Text = _fishingSuccesses.ToString();
            _failureValue.Text = failures.ToString();
            _rateValue.Text = _fishingRounds > 0 ? $"{Math.Round(_fishingSuccesses * 100.0 / _fishingRounds):0}%" : "0%";
            _averageValue.Text = _fishingSuccesses > 0 ? TimeSpan.FromSeconds(elapsed.TotalSeconds / _fishingSuccesses).ToString(@"mm\:ss") : "—";
            _startTimeValue.Text = _fishingStartedAt?.ToString("HH:mm:ss") ?? "—";
        }
    }


    private void TrackAlertHealthFromLog(string line)
    {
        string text = line;
        if ((_activeMode ?? SelectedMode) == "낚시")
        {
            bool progress = (text.Contains("낚싯대 아이콘") && text.Contains("-> Space"))
                || (text.Contains("나침반") && text.Contains("-> S"))
                || text.Contains("1차 게이지 감지")
                || text.Contains("챔질:")
                || text.Contains("다음 판")
                || text.Contains("판 종료");
            if (progress)
            {
                _lastFishingProgressAt = DateTime.Now;
                _fishingStallAlerted = false;
                _networkRetryCount = 0;
            }
        }

        if (text.Contains("네트워크 재시도", StringComparison.OrdinalIgnoreCase))
        {
            _networkRetryCount++;
            int limit = Math.Max(1, _notifier.Settings.NetworkRetryAlertCount);
            if (_networkRetryCount == limit)
                _ = SendRuntimeAlertAsync("네트워크 오류 반복", $"네트워크 재시도가 {_networkRetryCount}회 연속 발생했습니다. 현재 모드: {_activeMode ?? SelectedMode}", true);
        }

        if (text.Contains("[어비스 타임아웃] 퇴장 입력 시작"))
        {
            _exitProcedureDeadline = DateTime.Now.AddSeconds(Math.Max(30, _notifier.Settings.ExitProcedureTimeoutSeconds));
            _exitProcedureAlerted = false;
        }
        if (text.Contains("강제 퇴장 완료") || text.Contains("판 완료") || text.Contains("판 시작") || text.Contains("[어비스 퇴장 상태 초기화]") || text.Contains("[어비스 퇴장 절차 종료]"))
        {
            _exitProcedureDeadline = null;
            _exitProcedureAlerted = false;
            _networkRetryCount = 0;
        }
    }

    private void CheckAlertHealth()
    {
        if (_fishingBot.IsRunning)
        {
            int seconds = Math.Clamp(_notifier.Settings.FishingStallSeconds, 8, 10);
            if (!_fishingStallAlerted && (DateTime.Now - _lastFishingProgressAt).TotalSeconds >= seconds)
            {
                _fishingStallAlerted = true;
                _ = SendRuntimeAlertAsync("낚시 매크로 진행 정지 감지", $"{seconds}초 이상 정상 진행 신호가 없습니다.\n현재 상태: {_lastFishingStatus}", true);
            }
        }

        if (DungeonRunning && _exitProcedureDeadline.HasValue && !_exitProcedureAlerted && DateTime.Now >= _exitProcedureDeadline.Value)
        {
            _exitProcedureAlerted = true;
            _ = SendRuntimeAlertAsync("던전 강제 퇴장 지연", "10분 제한 후 시작된 퇴장 절차가 설정 시간 안에 완료되지 않았습니다.", true);
        }
    }

    private static T LoadJson<T>(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("설정 파일이 없습니다.", path);
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"JSON 읽기 실패: {path}");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FishingAutomation.NativeMethods.RegisterHotKey(Handle, HOTKEY_TEST, FishingAutomation.NativeMethods.MOD_NOREPEAT, FishingAutomation.NativeMethods.VK_F8);
        FishingAutomation.NativeMethods.RegisterHotKey(Handle, HOTKEY_START, FishingAutomation.NativeMethods.MOD_NOREPEAT, FishingAutomation.NativeMethods.VK_F9);
        FishingAutomation.NativeMethods.RegisterHotKey(Handle, HOTKEY_STOP, FishingAutomation.NativeMethods.MOD_NOREPEAT, FishingAutomation.NativeMethods.VK_F10);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        FishingAutomation.NativeMethods.UnregisterHotKey(Handle, HOTKEY_TEST);
        FishingAutomation.NativeMethods.UnregisterHotKey(Handle, HOTKEY_START);
        FishingAutomation.NativeMethods.UnregisterHotKey(Handle, HOTKEY_STOP);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (HandleReferenceResize(ref m)) return;
        if (m.Msg == FishingAutomation.NativeMethods.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HOTKEY_TEST) TestOrRefresh();
            else if (id == HOTKEY_START) StartSelected();
            else if (id == HOTKEY_STOP) StopSelected();
        }
        base.WndProc(ref m);
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    // Reference UI buttons are custom drawn. Never allow a click/menu exception
    // to escape the WinForms message loop and terminate the entire program.
    internal void SafeUiAction(string actionName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "ui-crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {actionName}\r\n{ex}\r\n\r\n");
            }
            catch { }

            try
            {
                _statusValue.Text = $"UI 오류: {ex.Message}";
                _statusValue.ForeColor = Color.Salmon;
                _miniStatus.Text = "UI 오류 · ui-crash.log 확인";
                _referenceDashboard?.Invalidate();
            }
            catch { }
        }
    }
}


