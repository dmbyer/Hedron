using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>
    /// Published after <c>areaset</c> sets a property on an area blueprint (template + live entity,
    /// INV-21). <paramref name="AreaEntityId"/> is 0 when no live area is spawned;
    /// <paramref name="NewValue"/> is the canonical text (<c>clear</c> for a removed designation).
    /// </summary>
    public sealed record AreaPropertySetByAdminEvent(
        uint AdminEntityId,
        uint AreaEntityId,
        string AreaBlueprintId,
        string PropertyName,
        string NewValue) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
