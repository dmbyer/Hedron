using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Effects.Events
{
    public sealed record EffectAppliedByAdminEvent(
        uint AdminId,
        uint TargetId,
        string EffectId,
        int Power) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
