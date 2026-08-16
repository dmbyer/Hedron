using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Systems
{
    public sealed class ProgressionSystem : IProgressionSystem
    {
        private static readonly IReadOnlyList<AwardOutcome> NoRows = Array.Empty<AwardOutcome>();

        private readonly EntityService _entityService;
        private readonly IRandom _random;
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IAdvancementRuleRegistry _rules;

        public ProgressionSystem(
            EntityService entityService,
            IRandom random,
            IPowerBudgetSystem powerBudget,
            IAdvancementRuleRegistry rules)
        {
            _entityService = entityService;
            _random = random;
            _powerBudget = powerBudget;
            _rules = rules;
        }

        // ── The one award entry point ────────────────────────────────────────────

        public UseAwardResult AwardUseExperience(uint entityId, XpSource source, UseAwardContext context)
        {
            if (!_rules.TryGet(source, out var rule))
                return new UseAwardResult(NoRows);

            // Context-level eligibility: a failure here means the action never qualified, so there
            // are no candidate tracks and nothing to report.
            var eligibility = rule.Eligibility;
            if (eligibility.RequiresAttributableActor && entityId == 0)
                return new UseAwardResult(NoRows);
            if (eligibility.RequiresPlayerEarner && !_entityService.HasComponent<CharacterComponent>(entityId))
                return new UseAwardResult(NoRows);
            if (eligibility.RequiresPositiveMagnitude && context.Magnitude <= 0)
                return new UseAwardResult(NoRows);

            var candidates = BuildCandidateTracks(rule, context);
            if (candidates.Count == 0)
                return new UseAwardResult(NoRows);

            // Candidate-level eligibility: an anti-grind ratio under the floor leaves the tracks in
            // play (the caller still sees a row per track, as it always has) but makes every
            // candidate ineligible — and an ineligible candidate consumes ZERO IRandom draws.
            var antiGrindScale = 1.0;
            var eligible = true;
            if (eligibility.AppliesAntiGrindPowerRatio)
            {
                antiGrindScale = ComputeAntiGrindScale(
                    GetEffectivePower(context.OpponentEntityId), GetEffectivePower(entityId));
                eligible = antiGrindScale > 0.0;
            }

            var rows = new List<AwardOutcome>(candidates.Count);
            foreach (var track in candidates)
            {
                var amount = 0;
                if (eligible && RollAward(rule, entityId, track))
                {
                    var baseAmount = _random.Next(rule.BaseAwardMin, rule.BaseAwardMax + 1);
                    amount = ScaleAward(baseAmount, rule, context.ContentScale, antiGrindScale);
                }

                rows.Add(AwardExperience(entityId, track, amount, source));
            }

            return new UseAwardResult(rows);
        }

        public CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId)
        {
            // The victim's per-mob XpScale is resolved HERE, not in the handler, so a live kill and
            // a sandbox kill (which calls this method directly) can never diverge.
            var contentScale = _entityService.TryGet<MobDataComponent>(victimEntityId, out var mob)
                ? mob.XpScale
                : 1.0;

            var result = AwardUseExperience(killerEntityId, XpSource.CombatKill, new UseAwardContext(
                ContentScale: contentScale,
                OpponentEntityId: victimEntityId));

            return new CombatAwardResult(result.Tracks);
        }

        // ── Accrual + threshold resolution ───────────────────────────────────────

        public AwardOutcome AwardExperience(uint entityId, ProgressionTrack track, int amount, XpSource source)
        {
            if (amount <= 0)
                return new AwardOutcome(track, 0, 0, GetImprovementCount(entityId, track));

            EnsureComponent(entityId, out var comp);

            comp.Xp.TryGetValue(track, out var xp);
            comp.Xp[track] = xp + amount;
            if (!comp.Improvements.ContainsKey(track))
                comp.Improvements[track] = 0;

            var improvementsGained = TryImprove(entityId, track);

            return new AwardOutcome(track, amount, improvementsGained, comp.Improvements[track]);
        }

        public AwardOutcome AwardExperience(uint entityId, ScoreId track, int amount, XpSource source)
            => AwardExperience(entityId, ProgressionTrack.Of(track), amount, source);

        public int TryImprove(uint entityId, ProgressionTrack track)
        {
            if (!_entityService.TryGet<ProgressionComponent>(entityId, out var comp))
                return 0;

            comp.Xp.TryGetValue(track, out var xp);
            comp.Improvements.TryGetValue(track, out var improvements);

            var gained = 0;
            while (xp >= NextThreshold(improvements))
            {
                improvements++;
                gained++;
            }

            if (gained > 0)
                comp.Improvements[track] = improvements;

            return gained;
        }

        public int TryImprove(uint entityId, ScoreId track)
            => TryImprove(entityId, ProgressionTrack.Of(track));

        // ── Reads ────────────────────────────────────────────────────────────────

        public int GetXp(uint entityId, ProgressionTrack track)
            => _entityService.TryGet<ProgressionComponent>(entityId, out var comp) && comp.Xp.TryGetValue(track, out var xp)
                ? xp
                : 0;

        public int GetXp(uint entityId, ScoreId track)
            => GetXp(entityId, ProgressionTrack.Of(track));

        public int GetImprovementCount(uint entityId, ProgressionTrack track)
            => _entityService.TryGet<ProgressionComponent>(entityId, out var comp) && comp.Improvements.TryGetValue(track, out var improvements)
                ? improvements
                : 0;

        public int GetImprovementCount(uint entityId, ScoreId track)
            => GetImprovementCount(entityId, ProgressionTrack.Of(track));

        public int GetXpToNextThreshold(uint entityId, ProgressionTrack track)
            => NextThreshold(GetImprovementCount(entityId, track)) - GetXp(entityId, track);

        public int GetXpToNextThreshold(uint entityId, ScoreId track)
            => GetXpToNextThreshold(entityId, ProgressionTrack.Of(track));

        public IReadOnlyList<ScoreId> GetTrackedScores(uint entityId)
            => GetTrackedTracks(entityId)
                .Where(track => track.IsScore)
                .Select(track => track.Score!.Value)
                .OrderBy(score => score)
                .ToList();

        public IReadOnlyList<ProgressionTrack> GetTrackedTracks(uint entityId)
        {
            if (!_entityService.TryGet<ProgressionComponent>(entityId, out var comp))
                return Array.Empty<ProgressionTrack>();

            return comp.Xp.Keys.Union(comp.Improvements.Keys).ToList();
        }

        // ── Internals ────────────────────────────────────────────────────────────

        private static int NextThreshold(int currentImprovementCount)
            => ProgressionConstants.ThresholdBase + currentImprovementCount * ProgressionConstants.ThresholdIncrement;

        /// <summary>
        /// The chance gate (R5). Rank decay makes use-based gain sub-linear in action count without
        /// touching the power step — the curve stays in the threshold, which remains the system's
        /// central slowing rule.
        ///
        /// <para>
        /// <b>Draw contract (INV-26).</b> A certainty (<c>&gt;= 1.0</c>) or an impossibility
        /// (<c>&lt;= 0.0</c>) is decided arithmetically and consumes <b>no</b> <c>IRandom</c> draw.
        /// The combat-kill row is a certainty, which is why kills draw exactly what they drew
        /// before this slice and every pinned simulation golden stays put.
        /// </para>
        /// </summary>
        private bool RollAward(AdvancementRule rule, uint entityId, ProgressionTrack track)
        {
            var improvements = GetImprovementCount(entityId, track);
            var decay = 1.0 + improvements * rule.ChanceDecayPerImprovement;
            var chance = Math.Clamp(decay <= 0.0 ? rule.BaseChance : rule.BaseChance / decay, 0.0, 1.0);

            if (chance >= 1.0) return true;
            if (chance <= 0.0) return false;

            return _random.NextDouble() < chance;
        }

        /// <summary>
        /// Composes the four tuning tiers (R6 + R7): the macro <c>GlobalXpScalar</c>, the
        /// per-source scale, the per-content scale (per-ability / per-mob), and the anti-grind
        /// ratio. Rounded away from zero, so a scaled award never silently vanishes to 0 through
        /// banker's rounding.
        /// </summary>
        private static int ScaleAward(int baseAmount, AdvancementRule rule, double contentScale, double antiGrindScale)
            => (int)Math.Round(
                baseAmount
                * ProgressionConstants.GlobalXpScalar
                * rule.SourceScale
                * contentScale
                * antiGrindScale,
                MidpointRounding.AwayFromZero);

        /// <summary>
        /// The ability's own track (when the rule takes one and the trigger named an ability),
        /// plus the attribute track — the subject's configured one, falling back to the rule's
        /// static tracks when the subject declares none.
        /// </summary>
        private static IReadOnlyList<ProgressionTrack> BuildCandidateTracks(AdvancementRule rule, UseAwardContext context)
        {
            var hasSubjectTrack = rule.IncludesSubjectTrack && !string.IsNullOrWhiteSpace(context.SubjectAbilityId);
            if (!hasSubjectTrack && context.SubjectAttributeTrack is null)
                return rule.StaticTracks;

            var candidates = new List<ProgressionTrack>(rule.StaticTracks.Count + 1);
            if (hasSubjectTrack)
                candidates.Add(ProgressionTrack.Ability(context.SubjectAbilityId!));

            if (context.SubjectAttributeTrack is { } attributeTrack)
                candidates.Add(ProgressionTrack.Of(attributeTrack));
            else
                candidates.AddRange(rule.StaticTracks);

            return candidates;
        }

        // Anti-grind scale — GetEffectivePower below builds the raw-attribute snapshot
        // IPowerBudgetSystem.Estimate scores. Below the floor ratio the candidate is ineligible
        // (0.0, no award and no draws); above 1.0 the scale is capped, never granting a windfall.
        private static double ComputeAntiGrindScale(int victimPower, int killerPower)
        {
            if (killerPower <= 0)
                return 0.0;

            var ratio = (double)victimPower / killerPower;
            if (ratio < ProgressionConstants.AntiGrindFloorRatio)
                return 0.0;

            return Math.Min(ratio, ProgressionConstants.AntiGrindCap);
        }

        // Raw base attributes, not IStatSystem.Get — reading the effect-folded value here would
        // create a DI cycle (StatSystem -> EffectSystem -> contributors -> ProgressionEffectContributor
        // -> IProgressionSystem -> ProgressionSystem -> IStatSystem). IPowerBudgetSystem is a core
        // system with no such cycle — the guard is that the *snapshot values* stay raw, not that
        // the oracle is un-injected.
        private int GetEffectivePower(uint entityId)
        {
            if (!_entityService.TryGet<AttributesComponent>(entityId, out var attrs))
                return 0;

            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = attrs.Mind,
                [ScoreId.Body] = attrs.Body,
                [ScoreId.Spirit] = attrs.Spirit,
                [ScoreId.Attunement] = attrs.Attunement,
            });
            return _powerBudget.Estimate(snapshot);
        }

        private void EnsureComponent(uint entityId, out ProgressionComponent comp)
        {
            if (!_entityService.TryGet<ProgressionComponent>(entityId, out comp))
            {
                comp = new ProgressionComponent();
                _entityService.AddComponent(entityId, comp);
            }
        }
    }
}
