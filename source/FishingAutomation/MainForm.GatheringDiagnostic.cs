namespace FishingAutomation;

public sealed partial class MainForm
{
    private bool _gatheringDiagnosticRunning;

    private async Task InspectGatheringCliAsync()
    {
        if (_gatheringDiagnosticRunning || AnyRunning || _starting || _productionPolling) return;
        string? name = _gatheringPage.SelectedName;
        if (name is null) { _log.Write("[CLI 검사] 품목을 선택하세요."); return; }
        _gatheringDiagnosticRunning = true;
        _starting = true; // Shared start guard prevents F9 from racing the diagnostic.
        _gatheringPage.UpdateExecution();
        _logExpanded = true; ApplyLogVisibility();
        try
        {
            await GatheringCliDiagnostic.RunAsync(name, _cli.CapabilitiesAsync,
                (filter, ct) => _cli.GetGatherableItemsAsync(filter, ct), _log.Write);
        }
        catch (Exception ex)
        { _log.Write("[CLI 검사] 조회 실패 · " + ex.GetType().Name); }
        finally
        {
            _starting = false;
            _gatheringDiagnosticRunning = false;
            if (!IsDisposed) _gatheringPage.UpdateExecution();
        }
    }
}

