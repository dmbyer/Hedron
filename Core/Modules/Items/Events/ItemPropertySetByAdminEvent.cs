using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Items.Events
{
    /// <summary>Published after an admin mutates an item property via <c>setitem</c>.</summary>
    public sealed record ItemPropertySetByAdminEvent(
        uint AdminEntityId,
        uint ItemEntityId,
        string PropertyName,
        string NewValue) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
