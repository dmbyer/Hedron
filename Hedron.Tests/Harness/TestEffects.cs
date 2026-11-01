using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Applies a fixed-magnitude <see cref="EffectKind.StatModifier"/> through the real
    /// <see cref="IEffectSystem.Apply"/> path — the same seam buffs, debuffs and the sim's gear
    /// residuals ride. <paramref name="durationSeconds"/> of -1 is permanent (<c>UntilRemoved</c>);
    /// a positive value is a timed effect that expires on <c>AdvanceTick</c>.
    /// </summary>
    public static class TestEffects
    {
        public static void ApplyStatModifier(
            IEffectSystem effects, uint entityId, ScoreId score, int magnitude,
            float durationSeconds = -1f, string? effectId = null)
        {
            var definition = new EffectDefinition(
                EffectId: effectId ?? $"test.{score}.{magnitude}",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(score, magnitude),
                Category: magnitude >= 0 ? EffectCategory.Buff : EffectCategory.Debuff,
                PowerScalingFormula: "fixed",
                Duration: durationSeconds,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal);
            effects.Apply(entityId, definition, entityId);
        }
    }
}
