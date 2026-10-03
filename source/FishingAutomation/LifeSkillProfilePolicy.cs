namespace FishingAutomation;

internal static class LifeSkillProfilePolicy
{
    internal const double BodyChangedProfileRatio = 0.18;
    internal const double StrongBodyChangedProfileRatio = 0.30;
    internal const double BottomBandChangedProfileRatio = 0.08;
    internal const double RetryUnchangedRatio = 0.035;

    internal static bool IsConfirmed(
        bool primaryStatOcr,
        bool lifeSkillLabelOcr,
        double bodyChangeRatio,
        double bottomBandChangeRatio)
    {
        if (primaryStatOcr)
            return true;

        if (lifeSkillLabelOcr &&
            bodyChangeRatio >= BodyChangedProfileRatio)
            return true;

        return bodyChangeRatio >= StrongBodyChangedProfileRatio &&
               bottomBandChangeRatio >= BottomBandChangedProfileRatio;
    }

    internal static bool MayRetryToggle(
        double bodyChangeRatio,
        double bottomBandChangeRatio)
        => bodyChangeRatio < RetryUnchangedRatio &&
           bottomBandChangeRatio < RetryUnchangedRatio;
}
