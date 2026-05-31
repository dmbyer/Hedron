using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Effects.Events
{
    public sealed record EffectAppliedEvent(
        uint TargetId,
        string EffectId,
        EffectCategory Category,
        int Power) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
