using System.Collections.Generic;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Ascension
{
    /// <summary>
    /// Folds the character-wide tier's additive power baseline into the effect modifier pipeline
    /// (the INV-24 contributor seam). Each tracked score's baseline
    /// (<see cref="AscensionConstants.TierBaselineStep"/> × tier) is derived on read from
    /// <see cref="IAscensionSystem"/> — never stored. Registered as a fourth
    /// <see cref="IEffectContributor"/> alongside <c>EquipmentEffectContributor</c>,
    /// <c>AbilityEffectContributor</c>, and <c>ProgressionEffectContributor</c>;
    /// <c>EffectSystem.GetModifiers</c> sums it, and <c>IStatSystem.Get</c> folds that on top of
    /// base + equipment + abilities + progression for every consumer.
    /// </summary>
    public sealed class AscensionEffectContributor : IEffectContributor
    {
        private readonly IAscensionSystem _ascension;

        public AscensionEffectContributor(IAscensionSystem ascension)
        {
            _ascension = ascension;
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
        {
            foreach (var tracked in AscensionConstants.TrackedScores)
            {
                if (tracked == scoreId)
                    return AscensionConstants.TierBaselineStep * _ascension.GetTier(entityId);
            }
            return 0;
        }

        public IEnumerable<Effect> GetActive(uint entityId)
        {
            var tier = _ascension.GetTier(entityId);
            if (tier <= 0)
                yield break;

            var power = AscensionConstants.TierBaselineStep * tier;
            foreach (var track in AscensionConstants.TrackedScores)
            {
                yield return new Effect(
                    $"ascension.{track}", EffectKind.StatModifier,
                    new EffectParams(track, power),
                    EffectCategory.Buff, power,
                    new EffectSource(entityId),
                    null, EffectLifetime.WhileKnown,
                    0f, 0f,
                    StackPolicy.Stack, EffectPhase.Normal);
            }
        }
    }
}
