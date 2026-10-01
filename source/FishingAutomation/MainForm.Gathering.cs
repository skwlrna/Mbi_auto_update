using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _gatheringDisplay = "품목·수량 선택";

    private async Task<GatheringPlan> AttachFreeGatheringSourceAsync(GatheringPlan plan, CancellationToken ct)
    {
        if (plan.SourceRecipe is not null) return plan;

        var recipes = await new AlteringCliData(_cli).RecipesAsync(ct);
        var candidateRows = recipes
            .Select((recipe, index) => new { recipe, index })
            .Where(x => x.recipe.MissingIngredients.Any(m => m.DisplayName == plan.DisplayName))
            .ToArray();
        if (candidateRows.Length == 0)
        {
            _log.Write("[자동 채집] 대상 재료를 포함한 가공 제법을 CLI에서 찾지 못했습니다. 재료 상세/구하는 방법 화면에서 시작합니다.");
            return plan;
        }

        var chosen = candidateRows
            .OrderBy(x => x.recipe.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.index)
            .First();
        string? facility = chosen.recipe.FacilityName;
        if (string.IsNullOrWhiteSpace(facility) || !AlteringPlan.Facilities.Contains(facility))
        {
            _log.Write("[자동 채집] 시작 가공 시설을 확인할 수 없어 재료 상세/구하는 방법 화면에서 시작합니다.");
            return plan;
        }

        int ordinal = recipes.Take(chosen.index + 1).Count(x => x.DisplayName == chosen.recipe.DisplayName);
        var source = new AlteringPlan(
            facility, chosen.recipe.DisplayName, 1, chosen.recipe.ProducedPerWork, false, ordinal);
        _log.Write($"[자동 채집] 무료 시작 경로 · {source.ScreenTitle} > {source.DisplayName} > {plan.DisplayName}");
        return plan with { SourceRecipe = source };
    }

    private async Task StartGatheringAsync()
    {
        _starting = true; _cancelStart = false;
        try
        {
            var plan = SelectedGatheringPlan();
            plan.Validate();
            string[] requiredCommands =
            {
                "get_my_info", "get_currencies", "get_gatherable_items", "get_activity",
                "get_inventory", "get_items", "get_alterable_items"
            };
            var capabilities = await CliAutomationGuards.EnsureCapabilitiesAsync(_cli, requiredCommands, CancellationToken.None);
            var confirmCommands = requiredCommands.Where(x => capabilities[x].RequiresConfirm).ToArray();
            _log.Write("[자동 채집] CLI capabilities 확인 완료 · 필수 명령 " + requiredCommands.Length + "개");
            if (confirmCommands.Length > 0)
                _log.Write("[자동 채집] requiresConfirm 명령 · " + string.Join(", ", confirmCommands) + " · 메인 화면 시작 버튼을 사용자 승인으로 사용합니다.");

            var data = new GatheringCliData(_cli);

            // Read-only schema probe. This runs before any screen input and only uses
            // get_gatherable_items, so it cannot spend Spirit Wings or start movement.
            var gatherableRaw = await _cli.GetGatherableItemsAsync(CancellationToken.None);
            if (!gatherableRaw.Success)
                throw new InvalidOperationException("채집 CLI 원본 구조 조회에 실패했습니다.");
            _log.Write("[자동 채집][CLI 구조] " +
                GatheringQueries.DescribeCatalogSchema(gatherableRaw, plan.DisplayName));

            plan = await AttachFreeGatheringSourceAsync(plan, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _productionLastMode = "채집";
            _productionDisplayName = plan.DisplayName;
            _productionTargetQuantity = plan.TargetQuantity;
            _productionCurrentQuantity = 0;
            _productionFacilityName = "자동 채집 경로";
            RefreshProductionDashboard();
            var identity = await CliIdentityGuard.CaptureAsync(_cli, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _log.Write("[자동 채집] 캐릭터 문맥 저장 · " + identity.Description);
            _gatheringPage.CharacterStatus = "확인됨";

            var windows = WindowTools.EnumerateVisibleWindows();
            if (windows.Count != 1)
                throw new InvalidOperationException("마비노기 모바일 창을 하나만 열어 주세요.");
            var settings = LoadJson<AppSettings>(Path.Combine(AppContext.BaseDirectory, "dungeon", "config", "appsettings.json"));
            WindowTools.EnsureClientSizeAndTopRight(windows[0].Handle, 800, 1000);
            await Task.Delay(500);
            if (_cancelStart || IsDisposed) return;

            var visual = new GatheringScreen(
                windows[0].Handle, settings, Path.Combine(AppContext.BaseDirectory, "debug", "gathering"), _cli, data);
            var screen = new ZeroWingGatheringScreen(visual, _cli, identity);
            var automation = new GatheringAutomation(data, screen);
            _dungeonCts?.Dispose(); _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "채집"; _mode.Enabled = false;
            _gatheringDisplay = $"{plan.DisplayName} {plan.TargetQuantity}개";
            _dungeonStartedAt = DateTime.Now; _dungeonStoppedAt = null;
            _dungeonCycles = _dungeonCompleted = 0; _runError = null;
            _inputValue.Text = _dungeonInputName = visual.InputMode;
            SetStatus("채집 시작 준비", Blue);
            visual.Log += text=>Ui(()=>_log.Write(text));
            screen.Log += text=>Ui(()=>_log.Write(text));
            automation.Log += text => Ui(() =>
            {
                _log.Write(text);
                _dungeonCycles = checked((int)Math.Min(automation.Gained,int.MaxValue));
                _productionCurrentQuantity = automation.Gained;
                SetStatus(text.Replace("[자동 채집] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            _log.Write("[자동 채집] 시작(F9) · " + _gatheringDisplay + " · CLI 조회 전용 / 일반 이동");
            _dungeonTask = Task.Run(async () =>
            {
                using (screen)
                {
                    try
                    {
                        await automation.RunAsync(plan, token);
                        Ui(() =>
                        {
                            _dungeonCompleted = checked((int)Math.Min(automation.Gained,int.MaxValue));
                            _productionCurrentQuantity = automation.Gained;
                            _log.Write("[자동 채집] 목표 작업 완료");
                            RefreshProductionDashboard();
                        });
                    }
                    catch (OperationCanceledException)
                    { Ui(() => _log.Write("[자동 채집] 정지되었습니다. 게임의 채집/이동 정지 여부를 확인하세요.")); }
                    catch (Exception ex)
                    {
                        Ui(() =>
                        {
                            _runError = ex.Message; _logExpanded = true; ApplyLogVisibility();
                            _log.Write("[자동 채집] 오류: " + ex.Message);
                            _ = SendRuntimeAlertAsync("자동 채집 오류", ex.Message, true);
                        });
                    }
                    finally
                    {
                        Ui(() =>
                        {
                            _dungeonStoppedAt = DateTime.Now; _activeMode = null; _mode.Enabled = true;
                            UpdateAbyssSelectorVisibility(); RefreshModeStatus();
                            if (_runError is not null) SetStatus("오류: " + _runError, Color.Salmon);
                            UpdateStats();
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _log.Write("[자동 채집] 시작 실패: " + ex.Message); SetStatus("채집 시작 실패", Color.Salmon);
            _ = SendRuntimeAlertAsync("자동 채집 시작 실패", ex.Message, true);
        }
        finally { _starting = false; }
    }
}
