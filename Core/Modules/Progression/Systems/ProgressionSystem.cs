using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Systems
{
    public sealed class ProgressionSystem : IProgressionSystem
    {
        private readonly EntityService _entityService;
        private readonly IRandom _random;
        private readonly IPowerBudgetSystem _powerBudget;

        public ProgressionSystem(EntityService entityService, IRandom random, IPowerBudgetSystem powerBudget)
        {
            _entityService = entityService;
            _random = random;
            _powerBudget = powerBudget;
        }

        public AwardOutcome AwardExperience(uint entityId, ScoreId track, int amount, XpSource source)
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

        public int TryImprove(uint entityId, ScoreId track)
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

        public CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId)
        {
            var killerPower = GetEffectivePower(killerEntityId);
            var victimPower = GetEffectivePower(victimEntityId);
            var scale = ComputeAntiGrindScale(victimPower, killerPower);

            var rows = new List<AwardOutcome>(ProgressionConstants.CombatTracks.Length);
            foreach (var track in ProgressionConstants.CombatTracks)
            {
                var amount = 0;
                if (scale > 0.0)
                {
                    var baseAmount = _random.Next(ProgressionConstants.CombatAwardMin, ProgressionConstants.CombatAwardMax + 1);
                    amount = (int)Math.Round(baseAmount * scale, MidpointRounding.AwayFromZero);
                }

                rows.Add(AwardExperience(killerEntityId, track, amount, XpSource.CombatKill));
            }

            return new CombatAwardResult(rows);
        }

        public int GetXp(uint entityId, ScoreId track)
            => _entityService.TryGet<ProgressionComponent>(entityId, out var comp) && comp.Xp.TryGetValue(track, out var xp)
                ? xp
                : 0;

        public int GetImprovementCount(uint entityId, ScoreId track)
            => _entityService.TryGet<ProgressionComponent>(entityId, out var comp) && comp.Improvements.TryGetValue(track, out var improvements)
                ? improvements
                : 0;

        public int GetXpToNextThreshold(uint entityId, ScoreId track)
            => NextThreshold(GetImprovementCount(entityId, track)) - GetXp(entityId, track);

        public IReadOnlyList<ScoreId> GetTrackedScores(uint entityId)
        {
            if (!_entityService.TryGet<ProgressionComponent>(entityId, out var comp))
                return Array.Empty<ScoreId>();

            return comp.Xp.Keys.Union(comp.Improvements.Keys).OrderBy(s => s).ToList();
        }

        // ── Internals ────────────────────────────────────────────────────────────

        private static int NextThreshold(int currentImprovementCount)
            => ProgressionConstants.ThresholdBase + currentImprovementCount * ProgressionConstants.ThresholdIncrement;

        // Anti-grind scale — GetEffectivePower below builds the raw-attribute snapshot
        // IPowerBudgetSystem.Estimate scores. Below the floor ratio the award rounds to zero;
        // above 1.0 the scale is capped, never granting a windfall.
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
