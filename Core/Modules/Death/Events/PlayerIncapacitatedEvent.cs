using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Death.Events
{
    /// <summary>
    /// Published when a player's HP crosses from positive to zero-or-below for the first time
    /// (i.e. the entity just became incapacitated). Thin payload — consumers that need room
    /// context read <see cref="Hedron.Core.ECS.Components.LocationComponent"/> directly.
    /// </summary>
    public sealed record PlayerIncapacitatedEvent(
        uint PlayerEntityId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
