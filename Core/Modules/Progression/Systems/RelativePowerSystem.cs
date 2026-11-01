using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Progression.Systems
{
    /// <inheritdoc cref="IRelativePowerSystem"/>
    public sealed class RelativePowerSystem : IRelativePowerSystem
    {
        private static readonly ScoreId[] AllScores = (ScoreId[])Enum.GetValues(typeof(ScoreId));

        private readonly EntityService _entityService;
        private readonly IStatSystem _statSystem;
        private readonly IPowerBudgetSystem _powerBudget;

        public RelativePowerSystem(EntityService entityService, IStatSystem statSystem, IPowerBudgetSystem powerBudget)
        {
            _entityService = entityService;
            _statSystem = statSystem;
            _powerBudget = powerBudget;
        }

        public int GetEffectivePower(uint entityId)
        {
            var scores = new Dictionary<ScoreId, int>();
            foreach (var score in AllScores)
                scores[score] = _statSystem.Get(entityId, score);

            // A mob's authored tier is the oracle's tier argument; a player's (or sandbox
            // combatant's) tier already rides IStatSystem.Get via the ascension contributor, and
            // a live mob never carries AscensionComponent — so tier is never counted twice.
            var tier = _entityService.TryGet<MobDataComponent>(entityId, out var mob) ? mob.Tier : 0;
            return _powerBudget.Estimate(new PowerSnapshot(scores), tier);
        }

        public RelativePowerReading Relate(uint subjectEntityId, uint targetEntityId)
        {
            if (!_entityService.HasComponent<AttributesComponent>(targetEntityId))
                return RelativePowerReading.Unreadable;

            return Relate(subjectEntityId, GetEffectivePower(targetEntityId));
        }

        public RelativePowerReading Relate(uint subjectEntityId, int targetPower)
        {
            if (!_entityService.HasComponent<AttributesComponent>(subjectEntityId))
                return RelativePowerReading.Unreadable;

            var subjectPower = GetEffectivePower(subjectEntityId);
            if (subjectPower <= 0)
                return RelativePowerReading.Unreadable;

            var clampedTarget = Math.Max(0, targetPower);
            var ratio = (double)clampedTarget / subjectPower;
            return new RelativePowerReading(true, subjectPower, clampedTarget, ratio, Classify(ratio));
        }

        public RelativePowerLabel Classify(double ratio)
        {
            if (ratio < ProgressionConstants.AntiGrindFloorRatio)
                return RelativePowerLabel.Trivial;
            if (ratio < ProgressionConstants.RelativeEvenFloorRatio)
                return RelativePowerLabel.Easy;
            if (ratio < ProgressionConstants.RelativeDangerousFloorRatio)
                return RelativePowerLabel.Even;
            if (ratio < ProgressionConstants.RelativeDeadlyFloorRatio)
                return RelativePowerLabel.Dangerous;
            return RelativePowerLabel.Deadly;
        }
    }
}
