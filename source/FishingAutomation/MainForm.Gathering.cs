using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private string _gatheringDisplay = "품목·수량 선택";

    private async Task<GatheringPlan> AttachFreeGatheringSourceAsync(GatheringPlan plan, CancellationToken ct)
    {
        if (plan.SourceRecipe is not null) return plan;

        bool hasDirect = LivingSkillGatheringCatalog.TryResolveBulk(plan.DisplayName, out var direct);
        if (hasDirect)
            _log.Write(
                $"[자동 채집] 생활 스킬 100회 직접 경로 · {direct.Category} > {direct.TargetName} · 가방 보조 경로용 시작 제법도 조회");

        var recipes = await new AlteringCliData(_cli).RecipesAsync(ct);
        var candidateRows = recipes
            .Select((recipe, index) => new { recipe, index })
            .Where(x => x.recipe.MissingIngredients.Any(m => m.DisplayName == plan.DisplayName))
            .ToArray();
        if (candidateRows.Length == 0)
        {
            if (hasDirect)
                _log.Write("[자동 채집] 가방 보조 경로용 시작 제법 없음 · 생활 스킬 100회 직접 경로만 사용");
            else
                _log.Write("[자동 채집] 생활 스킬 직접 매핑/보조 시작 제법 없음");
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
        _log.Write(hasDirect
            ? $"[자동 채집] 보조 시작 경로 준비 · {source.ScreenTitle} > {source.DisplayName} > {plan.DisplayName}"
            : $"[자동 채집] 무료 시작 경로 · {source.ScreenTitle} > {source.DisplayName} > {plan.DisplayName}");
        return plan with { SourceRecipe = source };
    }

    private async Task StartGatheringAsync()
    {
        _starting = true; _cancelStart = false;
        try
        {
            // Snapshot the standalone five-item queue on the UI thread before any awaits.
            // With no queue the historical single-item ADDITIONAL quantity is unchanged.
            var queuedTargets = _gatheringPage.QueuedGatheringPlans.ToArray();
            bool multiple = queuedTargets.Length > 0;
            if (queuedTargets.Length > 5)
                throw new InvalidOperationException("단독 순차채집은 최대 5종입니다.");
            var plan = multiple
                ? new GatheringPlan(queuedTargets[0].DisplayName, queuedTargets[0].TargetTotal)
                : SelectedGatheringPlan();
            plan.Validate();
            var selected = multiple
                ? queuedTargets
                : new[] { new GatheringQueueChoice(plan.DisplayName, plan.TargetQuantity) };
            if (selected.Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count() != selected.Length)
                throw new InvalidOperationException("순차채집 목록에 중복 품목이 있습니다.");
            foreach (var item in selected)
            {
                new GatheringPlan(item.DisplayName, item.TargetTotal).Validate();
                if (LivingSkillGatheringCatalog.IsQuestOnlyMaterial(item.DisplayName))
                    throw new InvalidOperationException(
                        $"{item.DisplayName}은(는) 곤충채집 퀘스트 전용 재료라 단독 자동채집에서는 시작하지 않습니다.");
                if (!LivingSkillGatheringCatalog.TryResolveBulk(item.DisplayName, out _))
                    throw new InvalidOperationException(
                        $"{item.DisplayName}은(는) 생활 스킬 대량채집 매핑이 확정되지 않아 시작하지 않습니다.");
            }
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
            // Verify EVERY queued name and tool from the fresh CLI catalog before game input.
            foreach (var item in selected)
            {
                var filtered = await _cli.GetGatherableItemsAsync(item.DisplayName, CancellationToken.None);
                var exact = GatheringQueries.ParseCatalog(filtered)
                    .SingleOrDefault(x => x.DisplayName == item.DisplayName);
                if (exact is null || !exact.ToolOk)
                    throw new InvalidOperationException($"CLI 직접검색에서 {item.DisplayName} 또는 채집 도구를 확인하지 못했습니다.");
                _log.Write($"[자동 채집][CLI 직접검색] query={item.DisplayName} · exact=1 · ToolOk=true");
            }
            // Preserve the original single-item source preflight. Multi targets use
            // the already-confirmed direct 100-action life-skill route, without recipe input.
            if (!multiple)
                plan = await AttachFreeGatheringSourceAsync(plan, CancellationToken.None);
            if (_cancelStart || IsDisposed) return;
            _productionLastMode = "채집";
            _productionDisplayName = multiple ? $"{selected.Length}종 순차채집" : plan.DisplayName;
            _productionTargetQuantity = multiple ? 0 : plan.TargetQuantity;
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

            var visual = new InventoryBulkGatheringScreen(
                windows[0].Handle, settings, Path.Combine(AppContext.BaseDirectory, "debug", "gathering"), _cli, data);
            var screen = new ZeroWingGatheringScreen(visual, _cli, identity);
            var managerState = new LifeSkillManagerState();
            managerState.Log += text => Ui(() => _log.Write(text));
            var automation = new GatheringAutomation(
                data, screen, managerState: managerState);
            var coordinator = new MultiGatheringCoordinator(data, screen, managerState: managerState);
            _dungeonCts?.Dispose(); _dungeonCts = new CancellationTokenSource();
            var token = _dungeonCts.Token;
            _activeMode = "채집"; _mode.Enabled = false;
            _gatheringDisplay = multiple
                ? $"{selected.Length}종 순차채집 · 목표 보유량 기준"
                : $"{plan.DisplayName} 추가 {plan.TargetQuantity}개";
            _dungeonStartedAt = DateTime.Now; _dungeonStoppedAt = null;
            _dungeonCycles = _dungeonCompleted = 0; _runError = null;
            _inputValue.Text = _dungeonInputName = visual.InputMode;
            SetStatus("채집 시작 준비", Blue);
            visual.Log += text=>Ui(()=>_log.Write(text));
            screen.Log += text=>Ui(()=>_log.Write(text));
            automation.Log += text => Ui(() =>
            {
                if (multiple) return;
                _log.Write(text);
                _dungeonCycles = checked((int)Math.Min(automation.Gained,int.MaxValue));
                _productionCurrentQuantity = automation.Gained;
                SetStatus(text.Replace("[자동 채집] ", ""), Blue);
                UpdateStats();
                RefreshProductionDashboard();
            });
            coordinator.Log += text => Ui(() =>
            {
                _log.Write(text);
                SetStatus(text.Replace("[다중 채집] ", "").Replace("[자동 채집] ", ""), Blue);
            });
            coordinator.Progress += (name, gained) => Ui(() =>
            {
                _dungeonCycles = checked((int)Math.Min(gained, int.MaxValue));
                _productionCurrentQuantity = gained;
                UpdateStats();
                RefreshProductionDashboard();
            });
            // Total-stock targets are backed by fresh get_items (inventory+both storage
            // locations). Existing single-item F9 still means ADD this many.
            if (multiple)
            {
                long remainingTotal = 0;
                foreach (var item in selected)
                {
                    long current = await data.ItemCountAsync(item.DisplayName, CancellationToken.None);
                    remainingTotal = checked(remainingTotal + Math.Max(0L, (long)item.TargetTotal - current));
                    _log.Write($"[순차채집] 목표 보유량 · {item.DisplayName} 현재 {current}, 목표 {item.TargetTotal}");
                }
                _productionTargetQuantity = checked((int)remainingTotal);
                RefreshProductionDashboard();
            }
            _log.Write("[자동 채집] 시작(F9) · " + _gatheringDisplay + " · CLI 조회 전용 / 일반 이동");
            _dungeonTask = Task.Run(async () =>
            {
                using var interruptions = ProductionInterruptionCheckpoint.Install(visual.CheckInterruptionsAsync, token);
                using (screen)
                {
                    try
                    {
                        if (multiple)
                        {
                            // One existing coordinator and one guarded Stop path, never
                            // one independent screen runner per queued material.
                            var requests = selected.Select(x =>
                                new MultiGatheringRequest(x.DisplayName, x.TargetTotal)).ToArray();
                            await coordinator.RunAsync(requests, token);
                            Ui(() =>
                            {
                                _dungeonCompleted = checked((int)Math.Min(_productionCurrentQuantity, int.MaxValue));
                                _log.Write("[순차채집] 전체 목표 보유량 확인 완료");
                                RefreshProductionDashboard();
                            });
                        }
                        else
                        {
                            managerState.BeginStage(LifeSkillStage.Gathering, "단독 채집 시작");
                            _ = await managerState.ReadFreshCountAsync(
                                LifeSkillStage.Gathering, plan.DisplayName,
                                data.ItemCountAsync, token);
                            await automation.RunAsync(plan, token);
                            _ = await managerState.ReadFreshCountAsync(
                                LifeSkillStage.Gathering, plan.DisplayName,
                                data.ItemCountAsync, token);
                            Ui(() =>
                            {
                                _dungeonCompleted = checked((int)Math.Min(automation.Gained, int.MaxValue));
                                _productionCurrentQuantity = automation.Gained;
                                _log.Write("[자동 채집] 목표 작업 완료");
                                RefreshProductionDashboard();
                            });
                        }
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
