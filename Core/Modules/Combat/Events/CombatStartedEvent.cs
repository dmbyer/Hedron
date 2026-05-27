using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Combat.Events
{
    public sealed record CombatStartedEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
