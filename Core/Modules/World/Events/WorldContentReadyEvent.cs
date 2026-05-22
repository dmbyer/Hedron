using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.World.Events
{
    /// <summary>
    /// Published by <c>WorldContentBootstrap</c> after <c>LoadAndSpawnAsync</c> completes
    /// — all authored rooms are spawned and <c>WorldConfiguration.StartingRoomEntityId</c>
    /// is resolved. Handlers that need valid room entities and a known starting room
    /// (e.g. <c>CharacterHydrationHandler</c>) subscribe here rather than to
    /// <c>WorldLoadedEvent</c>, which fires before world content has loaded.
    /// </summary>
    public record WorldContentReadyEvent : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
