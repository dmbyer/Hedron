using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// Balance constants for the progression substrate (configuration Category 3 —
    /// System Math/Balance). Co-located with <see cref="Systems.ProgressionSystem"/> so tuning
    /// changes land in the same commit as the code that reads them. Promotion to a tunable data
    /// file is deferred to a demonstrated need (OD-2) — the numbers here are pinned by CI
    /// simulation goldens, so a live-editable form needs that pinning contract reworked first.
    /// </summary>
    public static class ProgressionConstants
    {
        /// <summary>Linear power step granted per improvement, folded by <see cref="ProgressionEffectContributor"/>.</summary>
        public const int PowerPerImprovement = 2;

        /// <summary>Cumulative XP required to cross the first threshold (improvement 0 → 1).</summary>
        public const int ThresholdBase = 100;

        /// <summary>
        /// Growth added to the cumulative threshold per improvement already earned — the growing
        /// gap that slows the rate of gain while the power step itself stays linear.
        /// </summary>
        public const int ThresholdIncrement = 50;

        /// <summary>
        /// <b>The macro knob (R6).</b> Multiplies every awarded amount from every source before
        /// rounding. <c>1.0</c> is today's rate; <c>2.0</c> exactly doubles progression speed.
        /// Applied inside <c>ProgressionSystem</c> so no call site can bypass it.
        /// </summary>
        public const double GlobalXpScalar = 1.0;

        /// <summary>
        /// Anti-grind ratio floor: a victim-vs-killer power ratio below this makes the candidate
        /// <b>ineligible</b> (zero award, and zero <c>IRandom</c> draws — see
        /// <see cref="AdvancementEligibility.AppliesAntiGrindPowerRatio"/>).
        /// </summary>
        public const double AntiGrindFloorRatio = 0.25;

        /// <summary>Anti-grind ratio cap: the scale never exceeds this (no over-strong-victim windfall).</summary>
        public const double AntiGrindCap = 1.5;

        /// <summary>
        /// The score tracks a combat kill awards — physical-only (owner-accepted default). Kept as
        /// a named constant because the balance simulator's <c>progressionRate</c> scenario reduces
        /// per-track over exactly this list; the <see cref="XpSource.CombatKill"/> rule row is built
        /// from it so the two can never drift.
        /// </summary>
        public static readonly ScoreId[] CombatTracks = { ScoreId.Body, ScoreId.HpMax };

        /// <summary>
        /// <b>The advancement table (D2).</b> One row per wired <see cref="XpSource"/>; the single
        /// <c>AdvancementHandler</c> consults it through <see cref="Systems.IAdvancementRuleRegistry"/>
        /// rather than each source growing its own handler.
        ///
        /// <para>
        /// The <see cref="XpSource.CombatKill"/> row reproduces the pre-slice kill award exactly:
        /// the same tracks in the same order, the same 8–12 base range, the same anti-grind
        /// scaling — and, because its <c>BaseChance</c> is <c>1.0</c> with zero decay, the same
        /// <c>IRandom</c> draw sequence (the chance roll short-circuits without a draw, INV-26).
        /// </para>
        ///
        /// <para>
        /// The two use-based rows ship deliberately conservative (<c>BaseChance</c> well under 1,
        /// meaningful decay) because they feed <b>attribute</b> tracks, which do grant power, and
        /// the <c>progressionRate</c> simulation cannot yet see them — see the known gap recorded
        /// in <c>docs/design/balance.md</c>.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<AdvancementRule> Rules = BuildRules();

        private static AdvancementRule[] BuildRules()
        {
            var combatTracks = Array.ConvertAll(CombatTracks, ProgressionTrack.Of);

            return new[]
            {
                // ── Kill (pre-existing behaviour, re-expressed as row 1) ──────────────
                new AdvancementRule(
                    Source: XpSource.CombatKill,
                    StaticTracks: combatTracks,
                    IncludesSubjectTrack: false,
                    Eligibility: new AdvancementEligibility(
                        RequiresAttributableActor: true,
                        AppliesAntiGrindPowerRatio: true),
                    BaseAwardMin: 8,
                    BaseAwardMax: 12,
                    BaseChance: 1.0,
                    ChanceDecayPerImprovement: 0.0,
                    SourceScale: 1.0),

                // ── Ability use ───────────────────────────────────────────────────────
                // Candidates: the ability's own (display-only) track, plus the ability's
                // configured attribute track when it declares one — otherwise nothing, since
                // StaticTracks is empty. An ability with no XpAttributeTrack therefore grants
                // rank only and adds no attribute power.
                new AdvancementRule(
                    Source: XpSource.AbilityUse,
                    StaticTracks: Array.Empty<ProgressionTrack>(),
                    IncludesSubjectTrack: true,
                    Eligibility: new AdvancementEligibility(
                        RequiresAttributableActor: true,
                        RequiresPlayerEarner: true),
                    BaseAwardMin: 3,
                    BaseAwardMax: 6,
                    BaseChance: 0.25,
                    ChanceDecayPerImprovement: 0.15,
                    SourceScale: 1.0),

                // ── Damage taken ──────────────────────────────────────────────────────
                new AdvancementRule(
                    Source: XpSource.DamageTaken,
                    StaticTracks: combatTracks,
                    IncludesSubjectTrack: false,
                    Eligibility: new AdvancementEligibility(
                        RequiresAttributableActor: true,
                        RequiresPlayerEarner: true,
                        RequiresPositiveMagnitude: true),
                    BaseAwardMin: 2,
                    BaseAwardMax: 4,
                    BaseChance: 0.15,
                    ChanceDecayPerImprovement: 0.20,
                    SourceScale: 1.0),
            };
        }
    }
}
