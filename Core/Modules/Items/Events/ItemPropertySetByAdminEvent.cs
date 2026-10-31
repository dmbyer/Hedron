using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Items.Events
{
    /// <summary>
    /// Published after an admin sets an item blueprint property via <c>setitem</c>. The set fans out
    /// to the template and every live world instance (never <c>PersistentEntity</c> copies);
    /// <paramref name="InstancesUpdated"/> is how many world instances were updated (may be 0).
    /// </summary>
    public sealed record ItemPropertySetByAdminEvent(
        uint AdminEntityId,
        string BlueprintId,
        string PropertyName,
        string NewValue,
        int InstancesUpdated) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
