using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Death.Events
{
    /// <summary>
    /// Published each heartbeat tick while a player is incapacitated and losing HP to bleed.
    /// Carries the current HP and the floor so narration handlers can format a progress message.
    /// </summary>
    public sealed record PlayerBleedingEvent(
        uint PlayerEntityId,
        int CurrentHp,
        int HpFloor) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
