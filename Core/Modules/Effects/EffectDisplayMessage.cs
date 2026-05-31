using System;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Effects
{
    public sealed class EffectDisplayMessage : IOutputMessage
    {
        public string EffectId { get; }
        public EffectCategory EffectCategory { get; }
        public int Power { get; }
        public EffectLifetime Lifetime { get; }
        public float Duration { get; }
        public float Elapsed { get; }

        public OutputCategory Category => OutputCategory.Info;

        public EffectDisplayMessage(Effect effect)
        {
            EffectId = effect.EffectId;
            EffectCategory = effect.Category;
            Power = effect.Power;
            Lifetime = effect.Lifetime;
            Duration = effect.Duration;
            Elapsed = effect.Elapsed;
        }

        public string Format()
        {
            var sign = Power >= 0 ? "+" : "";
            var remaining = Lifetime == EffectLifetime.UntilRemoved
                ? "permanent"
                : $"{Math.Max(0f, Duration - Elapsed):F0}s remaining";
            return $"  {EffectId,-16} [{EffectCategory,-10}] {sign}{Power,4}  {remaining}";
        }
    }
}
