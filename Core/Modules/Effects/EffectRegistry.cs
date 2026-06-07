using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Effects
{
    public interface IEffectRegistry : IRegistry<string, EffectDefinition> { }

    public sealed class EffectRegistry : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
    {
        public EffectRegistry() : base(CreateRows(), d => d.EffectId) { }

        private static IEnumerable<EffectDefinition> CreateRows() => new EffectDefinition[]
        {
            new EffectDefinition(
                "empower", EffectKind.StatModifier,
                new EffectParams(ScoreId.Body, 5),
                EffectCategory.Buff, "fixed", 30f,
                StackPolicy.HighestWins, EffectPhase.Normal),

            new EffectDefinition(
                "weaken", EffectKind.StatModifier,
                new EffectParams(ScoreId.Body, -5),
                EffectCategory.Debuff, "fixed", 30f,
                StackPolicy.HighestWins, EffectPhase.Normal),

            new EffectDefinition(
                "regen", EffectKind.Periodic,
                new EffectParams(ScoreId.HpCurrent, 10),
                EffectCategory.Blessing, "fixed", 60f,
                StackPolicy.Stack, EffectPhase.Early),

            new EffectDefinition(
                "poison", EffectKind.Periodic,
                new EffectParams(ScoreId.HpCurrent, -8),
                EffectCategory.Poison, "fixed", 30f,
                StackPolicy.Stack, EffectPhase.Late),

            new EffectDefinition(
                "minor_curse", EffectKind.StatModifier,
                new EffectParams(ScoreId.Mind, -3),
                EffectCategory.Curse, "fixed", -1f,
                StackPolicy.Stack, EffectPhase.Normal),

            new EffectDefinition(
                "kick_damage", EffectKind.Instant,
                new EffectParams(ScoreId.HpCurrent, -15),
                EffectCategory.Debuff, "fixed", 0f,
                StackPolicy.Replace, EffectPhase.Normal),

            new EffectDefinition(
                "mend_heal", EffectKind.Instant,
                new EffectParams(ScoreId.HpCurrent, 20),
                EffectCategory.Buff, "fixed", 0f,
                StackPolicy.Replace, EffectPhase.Normal),

            new EffectDefinition(
                "toughness_passive", EffectKind.StatModifier,
                new EffectParams(ScoreId.HpMax, 20),
                EffectCategory.Buff, "fixed", 0f,
                StackPolicy.HighestWins, EffectPhase.Normal),
        };
    }
}
