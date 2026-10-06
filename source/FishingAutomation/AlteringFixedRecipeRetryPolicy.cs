namespace FishingAutomation;

internal static class AlteringFixedRecipeRetryPolicy
{
    internal static bool ShouldRetry(
        bool detailVisible,
        bool facilityVisible,
        bool moveButtonVisible,
        bool retryAlreadyUsed)
        => !detailVisible &&
           facilityVisible &&
           !moveButtonVisible &&
           !retryAlreadyUsed;
}
