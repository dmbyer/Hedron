using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Effects.Events
{
    public sealed record EffectExpiredEvent(
        uint TargetId,
        string EffectId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
