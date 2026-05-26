using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Mobs.Events
{
    public sealed record MobCreatedByAdminEvent(
        uint AdminEntityId,
        uint MobEntityId,
        string BlueprintId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
