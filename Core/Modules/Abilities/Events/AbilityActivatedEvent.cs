using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Abilities.Events
{
    public sealed record AbilityActivatedEvent(uint ActorEntityId, string AbilityId, uint? TargetEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
