using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Movement.Events
{
    /// <summary>Published after a player successfully moves from one room to another.</summary>
    public record PlayerMovedEvent(
        uint PlayerEntityId,
        uint FromRoomEntityId,
        uint ToRoomEntityId,
        Direction Direction) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
