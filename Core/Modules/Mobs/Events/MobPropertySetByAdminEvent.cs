using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Mobs.Events
{
    public sealed record MobPropertySetByAdminEvent(
        uint AdminEntityId,
        uint MobEntityId,
        string PropertyName,
        string NewValue) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
