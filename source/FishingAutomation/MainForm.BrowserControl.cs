namespace FishingAutomation;

public sealed partial class MainForm
{
    private BrowserControlServer? _browserControl;

    // Preserve the existing V3.3.42 explicit environment-variable opt-in.
    // A manual restart from the settings dialog uses a new random key.
    private void StartBrowserControl()
    {
        string? configuredToken = Environment.GetEnvironmentVariable("MABIAUTO_BROWSER_TOKEN");
        if (string.IsNullOrEmpty(configuredToken) || _browserControl?.IsRunning == true) return;
        try
        {
            StopBrowserControl();
            _browserControl = new BrowserControlServer(DispatchBrowserCommandAsync, configuredToken: configuredToken);
            _log.Write("[브라우저 제어] 로컬 연결 활성화");
        }
        catch (Exception ex)
        {
            StopBrowserControl();
            _log.Write("[브라우저 제어] 시작 실패: " + ex.GetType().Name);
        }
    }

    private void StopBrowserControl()
    {
        _browserControl?.Dispose();
        _browserControl = null;
    }

    private void ShowBrowserControlSettings()
    {
        using var dialog = new Form
        {
            Text = "브라우저 원격 제어", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(600, 330), MinimumSize = new Size(520, 300),
            Font = new Font("맑은 고딕", 10f), BackColor = Color.FromArgb(245,246,248),
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var help = new Label { Dock = DockStyle.Fill, Text =
            "기본 비활성화 · PC에서 직접 켜거나 기존 환경 변수로 활성화합니다. 휴대폰 접속은 별도 비공개 HTTPS 프록시가 필요하며 포트 공개는 금지합니다." };
        var address = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = BrowserControlServer.Address };
        var key = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "브라우저 연결 키" };
        var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var start = new Button { Text = "원격 제어 켜기", AutoSize = true };
        var stop = new Button { Text = "끄기 / 키 폐기", AutoSize = true };
        var close = new Button { Text = "닫기", AutoSize = true, DialogResult = DialogResult.OK };
        void RefreshState()
        {
            bool running = _browserControl?.IsRunning == true;
            key.Text = running ? _browserControl!.Token : "";
            start.Enabled = !running;
            stop.Enabled = running;
            status.Text = running ? "활성화 · 키를 외부에 공유하지 마세요." : "비활성화";
        }
        start.Click += (_, _) =>
        {
            try
            {
                StopBrowserControl();
                _browserControl = new BrowserControlServer(DispatchBrowserCommandAsync);
                RefreshState();
            }
            catch (Exception ex)
            {
                StopBrowserControl(); RefreshState();
                _log.Write("[브라우저 제어] 로컬 시작 실패: " + ex.GetType().Name);
                status.Text = "시작 실패 · PC 포트 및 HTTP 권한 확인 필요";
            }
        };
        stop.Click += (_, _) => { StopBrowserControl(); RefreshState(); };
        buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(close);
        layout.Controls.Add(help, 0, 0); layout.Controls.Add(address, 0, 1);
        layout.Controls.Add(key, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(buttons, 0, 4);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "주소와 키를 복사하세요. 환경 변수로 설정한 키는 PC 재시작 시 다시 활성화될 수 있습니다." }, 0, 5);
        dialog.Controls.Add(layout); dialog.CancelButton = close;
        RefreshState();
        dialog.ShowDialog(this);
    }

    private Task<string> DispatchBrowserCommandAsync(string command, string key, CancellationToken ct)
    {
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || Disposing || ct.IsCancellationRequested)
            return Task.FromResult("PC 연결이 종료되었습니다.");
        try
        {
            BeginInvoke(new Action(async () =>
            {
                if (IsDisposed || Disposing || ct.IsCancellationRequested)
                { reply.TrySetResult("PC 연결이 종료되었습니다."); return; }
                try
                {
                    bool browserStart = key.StartsWith("browser:", StringComparison.Ordinal) &&
                        (command == "가공 시작" || command.StartsWith("어비스 시작 ", StringComparison.Ordinal));
                    if (browserStart && CanResetBrowserPreviousAlteringBatch() &&
                        (command != "가공 시작" || (LoadPhoneAlteringQueue().Orders.Any(order => order.State == "시작 대기") &&
                            MultiAlteringRecentResume.Latest(BrowserAlteringBatchRoot) is null)))
                    {
                        var failure = await DungeonVisionBot.WindowTools.ActivateGameForRemoteStartAsync(ct);
                        if (failure is not null)
                        {
                            reply.TrySetResult(failure);
                            return;
                        }
                        ct.ThrowIfCancellationRequested();
                        if (IsDisposed || Disposing)
                        {
                            reply.TrySetResult("PC 연결이 종료되었습니다.");
                            return;
                        }
                    }
                    if (command == "/status") reply.TrySetResult(GetRemoteStatus());
                    else if (command == "/stop")
                    { StopSelected(); reply.TrySetResult("전체 정지 요청 접수"); }
                    else if (command == "브라우저 이전 배치")
                        reply.TrySetResult(GetBrowserPreviousAlteringBatch());
                    else if (command.StartsWith("브라우저 가공 초기화 ", StringComparison.Ordinal) &&
                             key.StartsWith("browser:", StringComparison.Ordinal))
                        reply.TrySetResult(ResetBrowserPreviousAlteringBatch(command["브라우저 가공 초기화 ".Length..]));
                    else if (command == "브라우저 상태 요약")
                        reply.TrySetResult(GetBrowserMobileState());
                    else if (command.StartsWith("브라우저 가공 목록 비우기 ", StringComparison.Ordinal))
                        reply.TrySetResult(ClearBrowserAlteringQueue(command["브라우저 가공 목록 비우기 ".Length..], key));
                    else if (command.StartsWith("브라우저 가공 취소 ", StringComparison.Ordinal))
                        reply.TrySetResult(CancelBrowserAlteringOrder(command["브라우저 가공 취소 ".Length..], key));
                    else if (command == "브라우저 가공 품목")
                        reply.TrySetResult(await GetBrowserAlteringCatalogAsync(ct));
                    else if (command == "브라우저 가공 대기열")
                        reply.TrySetResult(GetBrowserAlteringQueue());
                    else if (command == "가공 목록")
                        reply.TrySetResult(HandlePhoneAlteringCommand(command, "browser:read"));
                    else if (command.StartsWith("가공 ", StringComparison.Ordinal) && key.StartsWith("browser:", StringComparison.Ordinal))
                        reply.TrySetResult(HandlePhoneAlteringCommand(command, key));
                    else if (command.StartsWith("어비스 시작 ", StringComparison.Ordinal) && key.StartsWith("browser:", StringComparison.Ordinal))
                        reply.TrySetResult(StartBrowserAbyss(command["어비스 시작 ".Length..]));
                    else reply.TrySetResult("지원하지 않는 명령입니다.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                { reply.TrySetCanceled(ct); }
                catch (Exception ex)
                {
                    _log.Write("[브라우저 제어] 명령 실패: " + ex.GetType().Name);
                    reply.TrySetResult("명령을 처리하지 못했습니다. PC 상태를 확인하세요.");
                }
            }));
        }
        catch (InvalidOperationException) { reply.TrySetResult("PC 연결이 종료되었습니다."); }
        return reply.Task.WaitAsync(ct);
    }

    private static string BrowserAlteringBatchRoot =>
        Path.Combine(Path.GetDirectoryName(PhoneAlteringPath)!, "multi-altering");

    private bool CanResetBrowserPreviousAlteringBatch() =>
        !AnyRunning && _activeMode is null && !_gatheringDiagnosticRunning;

    private string ClearBrowserAlteringQueue(string revision, string key)
    {
        var queue = LoadPhoneAlteringQueue();
        if (queue.RecentMessages.Contains(key)) return "이미 접수한 명령입니다.";
        if (!CanResetBrowserPreviousAlteringBatch())
            return "PC 작업이 진행 중입니다. 정지 후 목록을 비워 주세요.";
        if (revision != _phoneAlteringRevision.ToString("N"))
            return "가공 목록이 변경되었습니다. 갱신된 목록을 확인해 주세요.";

        SavePhoneAlteringQueue(queue with
        {
            Next = 1,
            Orders = [],
            RecentMessages = queue.RecentMessages.Concat(queue.Orders.Select(order => order.MessageKey))
                .Append(key).Where(message => !string.IsNullOrEmpty(message)).Distinct().TakeLast(128).ToArray()
        });
        return "가공 목록을 비웠습니다. 다음 작업은 #1부터 등록됩니다.";
    }

    private string CancelBrowserAlteringOrder(string arguments, string key)
    {
        LoadPhoneAlteringQueue();
        string[] parts = arguments.Split(' ', 2);
        if (parts.Length != 2 || parts[0] != _phoneAlteringRevision.ToString("N"))
            return "가공 목록이 변경되었습니다. 갱신된 목록을 확인해 주세요.";
        return HandlePhoneAlteringCommand("가공 취소 " + parts[1], key);
    }

    private async Task<string> GetBrowserAlteringCatalogAsync(CancellationToken token)
    {
        if (!CanResetBrowserPreviousAlteringBatch())
            return System.Text.Json.JsonSerializer.Serialize(new { message = "PC 작업 종료 후 가공 품목을 불러와 주세요." });

        var recipes = await new AlteringCliData(_cli).RecipesAsync(token);
        var names = recipes.GroupBy(recipe => recipe.DisplayName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1 && AlteringFacilityResolver.Resolve(group.First()) is not null)
            .Select(group => group.Key)
            .Where(name => TelegramCommandParser.ParseAlteringOrder($"가공 {name} 1") is { } order &&
                order.Name == name)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();
        return System.Text.Json.JsonSerializer.Serialize(new { names });
    }

    private string GetBrowserAlteringQueue() =>
        System.Text.Json.JsonSerializer.Serialize(BrowserAlteringRows());

    private object BrowserAlteringRows() =>
        LoadPhoneAlteringQueue().Orders.OrderBy(order => order.Number)
            .Select(order => new
            {
                number = order.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                name = order.Name, total = order.Total, quantity = order.Quantity,
                state = order.State, detail = order.Detail
            }).ToArray();

    private string GetBrowserMobileState() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            status = GetRemoteStatus(),
            detail = _statusValue.Text,
            running = AnyRunning || _activeMode is not null || _gatheringDiagnosticRunning,
            phoneAltering = _phoneAlteringRun is not null,
            altering = _activeMode == "가공" && _multiAlteringRunning,
            abyss = _activeMode == "어비스",
            abyssPreparing = _starting && SelectedMode == "어비스",
            orders = BrowserAlteringRows(),
            queueRevision = _phoneAlteringRevision.ToString("N"),
            canClearQueue = CanResetBrowserPreviousAlteringBatch() &&
                (LoadPhoneAlteringQueue().Orders.Length > 0 || LoadPhoneAlteringQueue().Next != 1),
            previousBatch = BrowserPreviousAlteringBatch()
        });

    private string GetBrowserPreviousAlteringBatch() =>
        System.Text.Json.JsonSerializer.Serialize(BrowserPreviousAlteringBatch());

    private object BrowserPreviousAlteringBatch()
    {
        if (!CanResetBrowserPreviousAlteringBatch())
            return new { message = "PC 작업이 진행 중입니다. 종료 후 이전 배치를 확인할 수 있습니다." };
        var recent = MultiAlteringRecentResume.Latest(BrowserAlteringBatchRoot);
        return recent is null
            ? new { message = "이어할 최근 작업이 없습니다." }
            : new
            {
                id = recent.BatchId,
                startedAt = recent.StartedAt,
                plans = recent.Plans.Select(plan => new { name = plan.DisplayName, quantity = plan.TargetQuantity })
            };
    }

    private string ResetBrowserPreviousAlteringBatch(string expectedBatchId)
    {
        if (!CanResetBrowserPreviousAlteringBatch())
            return "PC 작업이 진행 중입니다. 작업 종료 후 초기화하세요.";
        string root = BrowserAlteringBatchRoot;
        using var lease = new MultiAlteringBatchStore(root);
        if (MultiAlteringRecentResume.Latest(root)?.BatchId != expectedBatchId)
            return "초기화 대상이 변경되었습니다. 이전 배치를 다시 확인하세요.";

        MultiAlteringRecentResume.HideLatest(root, expectedBatchId);
        _log.Write("[다중가공] 브라우저에서 최근 이어하기 대상 초기화 · " + expectedBatchId);
        return "이전 배치 초기화 완료 · 가공 시작을 누르면 대기열을 실행합니다.";
    }

    private string StartBrowserAbyss(string dungeon)
    {
        if (AnyRunning || _activeMode is not null || _gatheringDiagnosticRunning)
            return "다른 작업 진행 중 · 어비스 시작 차단";
        if (dungeon is not ("허상의 정박지" or "광기의 동굴" or "흩어진 물길"))
            return "지원하지 않는 던전입니다.";
        int index = _abyssDungeon.Items.IndexOf(dungeon);
        if (index < 0 || _referenceDashboard is null) return "던전 설정을 확인하세요.";
        _referenceDashboard.NavigateToMode(2);
        if (SelectedMode != "어비스") return "어비스 모드 선택 실패";
        _abyssDungeon.SelectedIndex = index;
        if (SelectedAbyssDungeon != dungeon) return "던전 선택 실패";
        if (!DungeonVisionBot.WindowTools.IsRequiredGameWindow(DungeonVisionBot.NativeMethods.GetForegroundWindow()))
            return DungeonVisionBot.WindowTools.RemoteFocusError;
        StartSelected();
        return _statusValue.Text;
    }
}
