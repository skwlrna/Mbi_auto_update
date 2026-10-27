namespace FishingAutomation;

public sealed partial class MainForm
{
    private readonly List<CraftingTelegramItem> _craftingTelegramItems = new();
    private int _craftingTelegramActiveIndex = -1;
    private string _craftingTelegramLastAction = "제작 실행 전";
    private DateTimeOffset _craftingTelegramLastActionAt;
    private bool _craftingTelegramRunning;
    private string? _craftingTelegramError;

    // Mutations are performed on the UI thread. The remote command only copies
    // the snapshot on that thread; it must never request game input or CLI work.
    private void BeginCraftingTelegramStatus(IReadOnlyList<CraftingPlan> plans)
    {
        _craftingTelegramItems.Clear();
        foreach (var plan in plans)
            _craftingTelegramItems.Add(new CraftingTelegramItem(
                plan.Category == CraftingCategory.Food ? "음식" : "아이템",
                plan.DisplayName, 0, plan.TargetQuantity, 0, plan.RequiredCrafts,
                CraftingTelegramPhase.Pending, "순서 대기", null));
        _craftingTelegramActiveIndex = -1;
        _craftingTelegramError = null;
        _craftingTelegramRunning = true;
        SetCraftingTelegramAction("제작 계획 준비 / 첫 품목 대기");
    }

    private void SetCraftingTelegramAction(string action)
    {
        if (!_craftingTelegramRunning || string.IsNullOrWhiteSpace(action)) return;
        _craftingTelegramLastAction = action.Trim();
        _craftingTelegramLastActionAt = DateTimeOffset.Now;
        int index = _craftingTelegramActiveIndex;
        if (index >= 0 && index < _craftingTelegramItems.Count &&
            _craftingTelegramItems[index].Phase == CraftingTelegramPhase.Active)
            _craftingTelegramItems[index] = _craftingTelegramItems[index] with
            {
                Stage = action.Trim()
            };
    }

    private void StartCraftingTelegramItem(int index)
    {
        if (!_craftingTelegramRunning || index < 0 || index >= _craftingTelegramItems.Count)
            return;
        _craftingTelegramActiveIndex = index;
        _craftingTelegramItems[index] = _craftingTelegramItems[index] with
        {
            Phase = CraftingTelegramPhase.Active,
            Stage = "제작 퀘스트 준비 / 재고 확인"
        };
        SetCraftingTelegramAction(
            $"{index + 1}/{_craftingTelegramItems.Count} · {_craftingTelegramItems[index].DisplayName} 제작 시작");
    }

    private void UpdateCraftingTelegramProgress(int index, CraftingProgress progress)
    {
        if (!_craftingTelegramRunning || index < 0 || index >= _craftingTelegramItems.Count)
            return;
        var item = _craftingTelegramItems[index];
        if (item.Phase != CraftingTelegramPhase.Active) return;
        _craftingTelegramItems[index] = item with
        {
            ConfirmedQuantity = Math.Clamp(progress.ConfirmedQuantity, 0, item.TargetQuantity),
            CompletedCrafts = progress.CompletedCrafts,
            RequiredCrafts = progress.RequiredCrafts,
            Stage = progress.Stage
        };
        // Batch transition is useful even when no log line is emitted.
        _craftingTelegramLastAction = item.DisplayName + " · " + progress.Stage;
        _craftingTelegramLastActionAt = DateTimeOffset.Now;
    }

    private void CompleteCraftingTelegramItem(int index, long confirmed, int completedCrafts)
    {
        if (index < 0 || index >= _craftingTelegramItems.Count) return;
        var item = _craftingTelegramItems[index];
        _craftingTelegramItems[index] = item with
        {
            Phase = CraftingTelegramPhase.Completed,
            ConfirmedQuantity = Math.Clamp(confirmed, 0, item.TargetQuantity),
            CompletedCrafts = completedCrafts,
            Stage = "재고 증가 확인 완료"
        };
        _craftingTelegramActiveIndex = -1;
        SetCraftingTelegramAction($"{index + 1}/{_craftingTelegramItems.Count} · {item.DisplayName} 목표 달성 확인");
    }

    private void SkipCraftingTelegramItem(int index, string reason)
    {
        if (index < 0 || index >= _craftingTelegramItems.Count) return;
        var item = _craftingTelegramItems[index];
        _craftingTelegramItems[index] = item with
        {
            Phase = CraftingTelegramPhase.Skipped,
            Stage = "재료 확보 실패 · 다음 품목으로 진행",
            Reason = reason
        };
        _craftingTelegramActiveIndex = -1;
        SetCraftingTelegramAction($"{index + 1}/{_craftingTelegramItems.Count} · {item.DisplayName} 재료 부족 건너뜀");
    }

    private void FinishCraftingTelegramStatus(string summary, string? error = null,
        CraftingTelegramPhase? interrupted = null)
    {
        int index = _craftingTelegramActiveIndex;
        if (interrupted.HasValue && index >= 0 && index < _craftingTelegramItems.Count)
        {
            var item = _craftingTelegramItems[index];
            if (item.Phase == CraftingTelegramPhase.Active)
                _craftingTelegramItems[index] = item with
                {
                    Phase = interrupted.Value,
                    Reason = error ?? summary
                };
        }
        _craftingTelegramActiveIndex = -1;
        _craftingTelegramRunning = false;
        _craftingTelegramError = error;
        _craftingTelegramLastAction = summary;
        _craftingTelegramLastActionAt = DateTimeOffset.Now;
    }

    private CraftingTelegramSnapshot? CaptureCraftingTelegramSnapshot()
    {
        if (InvokeRequired)
            return (CraftingTelegramSnapshot?)Invoke(
                new Func<CraftingTelegramSnapshot?>(CaptureCraftingTelegramSnapshot));
        if (_craftingTelegramItems.Count == 0 ||
            (_activeMode != "제작" && (AnyRunning || SelectedMode != "제작")))
            return null;

        return new CraftingTelegramSnapshot(
            _craftingTelegramItems.ToArray(),
            _craftingTelegramActiveIndex,
            _craftingTelegramLastAction,
            _craftingTelegramLastActionAt,
            _craftingTelegramRunning,
            _craftingTelegramError);
    }

    private string GetCraftingTelegramDetails()
    {
        var snapshot = CaptureCraftingTelegramSnapshot();
        return snapshot is null ? "" : CraftingTelegramStatus.Format(snapshot);
    }
}
