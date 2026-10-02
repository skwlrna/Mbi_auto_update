namespace FishingAutomation;

public sealed partial class MainForm
{
    private long _productionCurrentQuantity;
    private int _productionTargetQuantity;
    private string _productionDisplayName = "설정에서 선택";
    private string _productionFacilityName = "—";

    // V0.1.94 keeps gathering/altering inside the original reference dashboard.
    // These hooks now only refresh the shared dashboard; no second page is layered on top.
    private void UpdateProductionDashboardVisibility()
    {
        _referenceDashboard?.UpdateAbyssDungeonPickerVisibility();
        _referenceDashboard?.Invalidate();
    }

    private void RefreshProductionDashboard()
    {
        UpdateDashboard();
        _referenceDashboard?.Invalidate();
        if (_gatheringPage is null || _alteringPage is null || _craftingPage is null) return;
        _gatheringPage.UpdateExecution();
        _alteringPage.UpdateExecution();
        _craftingPage.UpdateExecution();
        if (_productionPageMode == "제작")
        {
            _ = RefreshCraftingStateAsync();
        }
        else if (_productionPageMode is not null && !_productionPolling && DateTime.UtcNow >= _productionNextPoll)
        {
            _ = RefreshProductionStateAsync(_productionPageMode == "가공" ? _alteringPage : _gatheringPage);
        }
    }
}
