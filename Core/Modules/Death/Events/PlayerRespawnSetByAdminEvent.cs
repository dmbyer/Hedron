using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Death.Events
{
    /// <summary>
    /// Published by <c>SetRespawnCommand</c> after a successful <c>setrespawn</c> operation.
    /// Used by <see cref="Hedron.Core.Modules.Admin.Handlers.AdminAuditHandler"/> for the
    /// structured audit log and by any future notification consumers.
    /// </summary>
    public sealed record PlayerRespawnSetByAdminEvent(
        uint AdminEntityId,
        uint PlayerEntityId,
        string RoomBlueprintId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
