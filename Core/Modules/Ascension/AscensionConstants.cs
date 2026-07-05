using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Ascension
{
    /// <summary>
    /// Balance constants for the ascension substrate (configuration Category 3 —
    /// System Math/Balance). Co-located with <see cref="Systems.AscensionSystem"/> so tuning
    /// changes land in the same commit as the code that reads them. Promotion to a tunable data
    /// file is deferred to a demonstrated need (OD-2) — mirrors <c>ProgressionConstants</c>.
    /// </summary>
    public static class AscensionConstants
    {
        /// <summary>Highest tier a character can reach.</summary>
        public const int MaxTier = 6;

        /// <summary>
        /// Flat additive power granted per tier for each tracked score, folded by
        /// <see cref="AscensionEffectContributor"/>. Tier 0 contributes exactly 0.
        /// </summary>
        public const int TierBaselineStep = 10;

        /// <summary>The scores the tier baseline applies to (mirrors <c>ProgressionConstants.CombatTracks</c>).</summary>
        public static readonly ScoreId[] TrackedScores = { ScoreId.Body, ScoreId.HpMax };

        /// <summary>
        /// Unlock ids configured for each tier, recorded onto <c>AscensionComponent.GrantedUnlocks</c>
        /// on a successful ascend to that tier. Empty in prog-2 — no tier configures unlocks yet;
        /// the grant-<i>execution</i> seam and concrete unlock content are deferred (see
        /// ascension.md Design notes). Keyed by tier; a missing key means no unlocks for that tier.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> UnlocksForTier =
            new Dictionary<int, IReadOnlyList<string>>();
    }
}
