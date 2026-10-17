using System.Numerics;

namespace DungeonVisionBot;

// Domain-level observation contract. Never map any field to a packet number or
// byte offset until a real permitted receive trace has verified that mapping.
internal sealed record WaterwayTrackedPlayer(
    string ObjectId, Vector2 Position, WaterwayEvidence IdentityEvidence,
    WaterwayEvidence PositionEvidence);

internal sealed record WaterwayObservedAim(
    string ThrowerObjectId, Vector2 ThrowerPosition, WaterwayEvidence ThrowerPositionEvidence,
    string AimedObjectId, string SpearElement, WaterwayEvidence AimEvidence);

internal sealed record WaterwayObservedShaman(
    string ObjectId, Vector2 Position, WaterwayEvidence PositionEvidence,
    string ShieldElement, WaterwayEvidence ShieldEvidence,
    bool IsAlive, WaterwayEvidence LifeEvidence);

internal readonly record struct WaterwaySpearPlan(
    string ThrowerObjectId, string ShamanObjectId, string SpearElement,
    Vector2 Target, long SelectionRevision, DateTimeOffset AimObservedAt);

// Plan only after actual observed aim-to-self and matching current shield.
// Freshness threshold and behind distance MUST come from measured capture/movement behavior.
internal static class WaterwaySpearPlanner
{
    public static WaterwaySpearPlan? TryChoose(
        WaterwayTrackedPlayer self,
        WaterwayObservedAim aim,
        IReadOnlyList<WaterwayObservedShaman> shamans,
        DateTimeOffset now, TimeSpan measuredFreshness,
        float measuredSetback, long observationRevision)
    {
        if (string.IsNullOrWhiteSpace(self.ObjectId) ||
            string.IsNullOrWhiteSpace(aim.ThrowerObjectId) ||
            string.IsNullOrWhiteSpace(aim.AimedObjectId) ||
            string.IsNullOrWhiteSpace(aim.SpearElement) ||
            aim.AimedObjectId != self.ObjectId ||
            !self.IdentityEvidence.IsFresh(now, measuredFreshness) ||
            !self.PositionEvidence.IsFresh(now, measuredFreshness) ||
            !aim.ThrowerPositionEvidence.IsFresh(now, measuredFreshness) ||
            !aim.AimEvidence.IsFresh(now, measuredFreshness))
            return null;

        WaterwayObservedShaman? best = null;
        Vector2? chosenPosition = null;
        float distanceSquared = float.PositiveInfinity;
        foreach (WaterwayObservedShaman shaman in shamans)
        {
            if (!shaman.IsAlive || string.IsNullOrWhiteSpace(shaman.ObjectId) ||
                !string.Equals(shaman.ShieldElement, aim.SpearElement, StringComparison.Ordinal) ||
                !shaman.PositionEvidence.IsFresh(now, measuredFreshness) ||
                !shaman.ShieldEvidence.IsFresh(now, measuredFreshness) ||
                !shaman.LifeEvidence.IsFresh(now, measuredFreshness))
                continue;

            Vector2? candidate = WaterwaySpearGeometry.TargetBehind(
                aim.ThrowerPosition, shaman.Position, measuredSetback);
            if (candidate is null) continue;
            float candidateDistance = Vector2.DistanceSquared(aim.ThrowerPosition, shaman.Position);
            if (candidateDistance >= distanceSquared) continue;
            best = shaman;
            chosenPosition = candidate;
            distanceSquared = candidateDistance;
        }
        return best is null || chosenPosition is null ? null :
            new WaterwaySpearPlan(aim.ThrowerObjectId, best.ObjectId,
                aim.SpearElement, chosenPosition.Value, observationRevision,
                aim.AimEvidence.ObservedAt);
    }
}

internal enum WaterwaySpearOutcome
{
    SpearLaunched, ShamanHit, ShieldChanged, ShamanStaggered, ShamanDefeated
}

internal static class WaterwaySpearSuccess
{
    public static bool IsVerified(
        WaterwaySpearPlan plan, string observedAffectedShamanId,
        WaterwaySpearOutcome outcome, WaterwayEvidence proof)
    {
        if (!proof.IsDirect || proof.ObservedAt < plan.AimObservedAt ||
            !string.Equals(plan.ShamanObjectId,
                observedAffectedShamanId, StringComparison.Ordinal))
            return false;
        return outcome is WaterwaySpearOutcome.ShamanHit
            or WaterwaySpearOutcome.ShieldChanged
            or WaterwaySpearOutcome.ShamanStaggered
            or WaterwaySpearOutcome.ShamanDefeated;
    }
}
