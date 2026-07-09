using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Balance constants for <see cref="PowerBudgetSystem"/> (configuration Category 3 —
    /// System Math/Balance, see docs/architecture/05-configuration.md). Co-located with the
    /// system so tuning changes land in the same commit as the code that reads them. Promotion
    /// to a tunable data file is deferred to a demonstrated need (OD-2 — the likely trigger is
    /// the prog-4 simulation harness driving heavy iteration).
    /// </summary>
    public static class PowerBudgetConstants
    {
        /// <summary>
        /// Full <see cref="ScoreId"/> → weight table. Combat-relevant scores (<c>Body</c>,
        /// <c>HpMax</c>, <c>AttackPower</c>, <c>Defense</c>) carry meaningful positive weights;
        /// pools and current-value scores carry light-or-zero weights (P2). A score with no
        /// entry contributes 0. Heuristic and tunable — not a precise truth.
        /// Recalibrated for the Tier×Band revision (slice prog-3b): <c>Body</c>/<c>HpMax</c> —
        /// the two <see cref="TrackedScores"/> the tier baseline applies to — carry higher weight
        /// so the per-tier power step (<see cref="TierBaselineStep"/> × these weights) comfortably
        /// exceeds <c>3 × <see cref="BandSpan"/></c>, giving the 3-band subdivision (see
        /// <c>docs/design/power-model.md</c>) real headroom to spread across. Oracle-estimation
        /// only — does not touch <c>AscensionConstants</c>/gameplay power (deferred to prog-4).
        /// </summary>
        public static readonly IReadOnlyDictionary<ScoreId, int> Weights = new Dictionary<ScoreId, int>
        {
            [ScoreId.Mind] = 1,
            [ScoreId.Body] = 10,
            [ScoreId.Spirit] = 1,
            [ScoreId.Attunement] = 1,
            [ScoreId.HpMax] = 2,
            [ScoreId.ManaMax] = 0,
            [ScoreId.StaminaMax] = 0,
            [ScoreId.AstraMax] = 0,
            [ScoreId.HpCurrent] = 0,
            [ScoreId.ManaCurrent] = 0,
            [ScoreId.StaminaCurrent] = 0,
            [ScoreId.AstraCurrent] = 0,
            [ScoreId.AttackPower] = 8,
            [ScoreId.Defense] = 8,
        };

        /// <summary>
        /// Width of the deliberate overlap between adjacent tier bands (Ascension semantics — a
        /// maxed lower-tier build can reach into the next band before formally ascending). Must
        /// stay below a third of the per-tier power step (<c>Σ weight[TrackedScores] ×
        /// TierBaselineStep</c>) — <c>BandSpan &lt; tierSpan / BandsPerTier</c> — or the 3-band
        /// subdivision would stop being strictly ordered. With the recalibrated <see cref="Weights"/>
        /// the per-tier step is 120, so 20 leaves each 40-wide band comfortable headroom.
        /// </summary>
        public const int BandSpan = 20;

        /// <summary>
        /// Number of descriptive bands (low/mid/high) each tier subdivides into. Fixed at 3 — the
        /// "feels meaningful without introducing a whole leveling system" shape from the Tier×Band
        /// revision (slice prog-3b); not a mirror of any domain constant.
        /// </summary>
        public const int BandsPerTier = 3;

        /// <summary>
        /// Constant snapshot mirroring the canonical new-character starting stat block
        /// (<c>CharacterDefaultsOptions</c> in <c>Core/Modules/Account/</c>: attributes 10, HpMax
        /// 100, Mana/Stamina 50, Astra 10) plus the same base derivations <c>IStatSystem</c> uses
        /// (<c>AttackPower = Body/2</c>, <c>Defense = Body/4</c>). Held as a co-located constant
        /// — not an injected <c>IOptions&lt;CharacterDefaultsOptions&gt;</c> — so the core oracle
        /// takes no dependency on the domain <c>Account</c> module (INV-2). Keep in sync with
        /// <c>CharacterDefaultsOptions</c>.
        /// </summary>
        public static readonly IReadOnlyDictionary<ScoreId, int> ReferenceBaseScores = new Dictionary<ScoreId, int>
        {
            [ScoreId.Mind] = 10,
            [ScoreId.Body] = 10,
            [ScoreId.Spirit] = 10,
            [ScoreId.Attunement] = 10,
            [ScoreId.HpMax] = 100,
            [ScoreId.ManaMax] = 50,
            [ScoreId.StaminaMax] = 50,
            [ScoreId.AstraMax] = 10,
            [ScoreId.AttackPower] = 5,
            [ScoreId.Defense] = 2,
        };

        /// <summary>
        /// Mirrors <c>AscensionConstants.MaxTier</c> (<c>Core/Modules/Ascension/</c>) — the highest
        /// tier a character can reach, and the upper bound <see cref="PowerBudgetSystem.Classify"/>
        /// enumerates bands over. Held as a co-located constant — not a reference into the domain
        /// <c>Ascension</c> module — so the core oracle takes no dependency on it (INV-2, same
        /// rationale as <see cref="ReferenceBaseScores"/> mirroring <c>CharacterDefaultsOptions</c>).
        /// Keep in sync with <c>AscensionConstants.MaxTier</c>.
        /// </summary>
        public const int MaxTier = 6;

        /// <summary>
        /// Mirrors <c>AscensionConstants.TierBaselineStep</c> — the flat additive power granted per
        /// tier for each of <see cref="TrackedScores"/>. Keep in sync with
        /// <c>AscensionConstants.TierBaselineStep</c>.
        /// </summary>
        public const int TierBaselineStep = 10;

        /// <summary>
        /// Mirrors <c>AscensionConstants.TrackedScores</c> — the scores the tier baseline applies
        /// to. Keep in sync with <c>AscensionConstants.TrackedScores</c>.
        /// </summary>
        public static readonly ScoreId[] TrackedScores = { ScoreId.Body, ScoreId.HpMax };
    }
}
