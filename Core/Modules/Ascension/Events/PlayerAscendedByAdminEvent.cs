using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Ascension.Events
{
    /// <summary>
    /// Published by <c>AscendCommand</c> after a successful admin-triggered <c>ascend</c>.
    /// Used by <see cref="Hedron.Core.Modules.Admin.Handlers.AdminAuditHandler"/> for the
    /// structured audit log. Mirrors <c>PlayerRespawnSetByAdminEvent</c>.
    /// </summary>
    public sealed record PlayerAscendedByAdminEvent(
        uint AdminEntityId,
        uint TargetEntityId,
        int NewTier) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
