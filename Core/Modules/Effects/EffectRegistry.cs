using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Effects
{
    public interface IEffectRegistry
    {
        bool TryGet(string effectId, out EffectDefinition definition);
        IReadOnlyCollection<string> AllIds { get; }
    }

    public sealed class EffectRegistry : IEffectRegistry
    {
        private static readonly Dictionary<string, EffectDefinition> _definitions = new()
        {
            ["empower"] = new EffectDefinition(
                "empower", EffectKind.StatModifier,
                new EffectParams(ScoreId.Body, 5),
                EffectCategory.Buff, "fixed", 30f,
                StackPolicy.HighestWins, EffectPhase.Normal),

            ["weaken"] = new EffectDefinition(
                "weaken", EffectKind.StatModifier,
                new EffectParams(ScoreId.Body, -5),
                EffectCategory.Debuff, "fixed", 30f,
                StackPolicy.HighestWins, EffectPhase.Normal),

            ["regen"] = new EffectDefinition(
                "regen", EffectKind.Periodic,
                new EffectParams(ScoreId.HpCurrent, 10),
                EffectCategory.Blessing, "fixed", 60f,
                StackPolicy.Stack, EffectPhase.Early),

            ["poison"] = new EffectDefinition(
                "poison", EffectKind.Periodic,
                new EffectParams(ScoreId.HpCurrent, -8),
                EffectCategory.Poison, "fixed", 30f,
                StackPolicy.Stack, EffectPhase.Late),

            ["minor_curse"] = new EffectDefinition(
                "minor_curse", EffectKind.StatModifier,
                new EffectParams(ScoreId.Mind, -3),
                EffectCategory.Curse, "fixed", -1f,
                StackPolicy.Stack, EffectPhase.Normal),
        };

        public bool TryGet(string effectId, out EffectDefinition definition)
            => _definitions.TryGetValue(effectId, out definition!);

        public IReadOnlyCollection<string> AllIds => _definitions.Keys;
    }
}
