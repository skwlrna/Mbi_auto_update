namespace DungeonVisionBot;

internal static class ProductionSearchFlow
{
    /// <summary>
    /// Shared in-game search contract.
    /// The game does not commit typed item text until Enter is pressed, so every
    /// production search uses exactly:
    /// input field -> paste -> Enter -> Apply -> exact result.
    /// The caller is responsible only for opening the search dialog/magnifier.
    /// </summary>
    internal static async Task SearchAndSelectAsync(
        ProductionUiRuntime ui,
        string placeholder,
        Rectangle dialogArea,
        string query,
        Rectangle resultArea,
        CancellationToken ct,
        string context,
        Action<string>? log = null)
    {
        _ = await ui.ClickStableExactAndPasteAsync(
            placeholder,
            dialogArea,
            query,
            ct,
            $"{context} 검색 입력칸을 확인하지 못했습니다.",
            dimText: true);

        await Task.Delay(120, ct);

        ui.TapFresh(0x1C, ct); // Enter is mandatory after every item/material/product query.
        log?.Invoke($"[{context}] 검색어 입력 확정 · Enter · {query}");

        await Task.Delay(220, ct);

        _ = await ui.ClickStableExactAsync(
            "적용하기",
            dialogArea,
            ct,
            $"{context} Enter 입력 후 적용하기 버튼을 확인하지 못했습니다.",
            dimText: true);

        await Task.Delay(650, ct);

        _ = await ui.ClickStableExactAsync(
            query,
            resultArea,
            ct,
            $"{context} 검색 결과에서 정확한 {query} 항목을 찾지 못했습니다.",
            dimText: true);
    }
}
