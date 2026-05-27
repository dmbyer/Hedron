using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Combat.Events
{
    public sealed record CombatRoundEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        uint RoomEntityId,
        CombatRoundResult Result) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
