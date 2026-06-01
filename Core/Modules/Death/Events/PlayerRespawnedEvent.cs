using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Death.Events
{
    /// <summary>
    /// Published after <see cref="Hedron.Core.Modules.Death.Systems.IDeathSystem.Respawn"/> has
    /// completed — the player is now at their respawn room with pools partially restored and
    /// impermanent effects cleared.
    /// </summary>
    public sealed record PlayerRespawnedEvent(
        uint PlayerEntityId,
        uint RespawnRoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
