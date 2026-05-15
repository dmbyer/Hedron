using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin spawns a templated entity via <c>@spawn</c>.</summary>
    public record EntitySpawnedByAdminEvent(
        uint AdminEntityId,
        uint SpawnedEntityId,
        string BlueprintId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
