namespace FishingAutomation;

// F07/F08: managed processing should require two independent positive detail
// identity observations across an await boundary. Any missing/contradictory
// observation fails closed. This policy does not itself inject UI input.
internal static class AlteringRecipeIdentityPolicy
{
    internal static bool IsNearMedicineFirstResult(
        int observedX, int observedY, int expectedX, int expectedY)
        => Math.Abs(observedX - expectedX) <= 180 &&
           Math.Abs(observedY - expectedY) <= 65;

    internal static async Task<bool> VerifyTwoFreshObservationsAsync(
        Func<CancellationToken, Task<bool>> observeExpectedDetail,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observeExpectedDetail);
        ArgumentNullException.ThrowIfNull(delay);
        ct.ThrowIfCancellationRequested();
        if (!await observeExpectedDetail(ct))
            return false;
        await delay(TimeSpan.FromMilliseconds(180), ct);
        ct.ThrowIfCancellationRequested();
        return await observeExpectedDetail(ct);
    }
}
