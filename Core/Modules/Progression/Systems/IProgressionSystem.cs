using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression.Systems
{
    /// <summary>Per-track outcome of a single <see cref="IProgressionSystem.AwardExperience"/> call.</summary>
    public readonly record struct AwardOutcome(
        ScoreId Track,
        int AmountAwarded,
        int ImprovementsGained,
        int NewImprovementCount);

    /// <summary>Per-track outcome of a combat kill award, one row per <see cref="ProgressionConstants.CombatTracks"/>.</summary>
    public sealed record CombatAwardResult(IReadOnlyList<AwardOutcome> Tracks);

    /// <summary>
    /// Use-driven experience accrual and threshold-improvement for the progression substrate
    /// (gameplay-model Spine E). A track is keyed directly by <see cref="ScoreId"/>. Reads raw
    /// <c>AttributesComponent</c> fields directly (not <c>IStatSystem</c>) for the anti-grind
    /// power proxy — going through <c>IStatSystem</c> would create a DI cycle back through the
    /// <c>IEffectContributor</c> port this system's own contributor registers on. Never touches
    /// the event bus (INV-5) — callers publish the result.
    /// </summary>
    public interface IProgressionSystem
    {
        /// <summary>
        /// Adds <paramref name="amount"/> to <paramref name="track"/>'s cumulative XP for
        /// <paramref name="entityId"/>, creating the component/track entry on first award, then
        /// resolves any threshold crossings. A non-positive <paramref name="amount"/> is a no-op —
        /// no entry is created and <see cref="AwardOutcome.ImprovementsGained"/> is 0.
        /// </summary>
        AwardOutcome AwardExperience(uint entityId, ScoreId track, int amount, XpSource source);

        /// <summary>
        /// Increments <paramref name="track"/>'s improvement count once per threshold the
        /// entity's current cumulative XP has crossed (a single large award can cross several).
        /// Returns the number of improvements gained by this call. Safe to call on an entity with
        /// no <see cref="Components.ProgressionComponent"/> — returns 0, creates nothing.
        /// </summary>
        int TryImprove(uint entityId, ScoreId track);

        /// <summary>
        /// Resolves a combat-kill award: computes the killer-vs-victim anti-grind scale, rolls a
        /// randomized per-track base amount (via the injected <c>IRandom</c>), scales it, and
        /// awards each of <see cref="ProgressionConstants.CombatTracks"/> via
        /// <see cref="AwardExperience"/>. Publishes nothing (INV-5) — the caller publishes events
        /// per row.
        /// </summary>
        CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId);

        /// <summary>Cumulative XP earned so far for <paramref name="track"/>. 0 if never awarded.</summary>
        int GetXp(uint entityId, ScoreId track);

        /// <summary>Number of thresholds crossed for <paramref name="track"/>. 0 if never awarded.</summary>
        int GetImprovementCount(uint entityId, ScoreId track);

        /// <summary>Cumulative XP still needed to cross the next threshold for <paramref name="track"/>.</summary>
        int GetXpToNextThreshold(uint entityId, ScoreId track);

        /// <summary>Every track the entity has ever been awarded XP or an improvement on.</summary>
        IReadOnlyList<ScoreId> GetTrackedScores(uint entityId);
    }
}
