using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Death.Events
{
    /// <summary>
    /// Published when a player's HP reaches or drops below <c>Death:HpFloor</c>.
    /// <c>KillerEntityId == 0</c> is the "no attributable killer" sentinel (e.g. bleed death).
    /// </summary>
    public sealed record PlayerDiedEvent(
        uint PlayerEntityId,
        uint DeathRoomEntityId,
        uint KillerEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
