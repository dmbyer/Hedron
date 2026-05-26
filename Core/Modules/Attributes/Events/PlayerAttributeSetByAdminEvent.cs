using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Attributes.Events
{
    public sealed record PlayerAttributeSetByAdminEvent(
        uint AdminEntityId,
        uint PlayerEntityId,
        string PropertyName,
        string NewValue) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
