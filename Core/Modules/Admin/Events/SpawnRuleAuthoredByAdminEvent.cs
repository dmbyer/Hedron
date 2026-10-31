using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>What an admin did to a room spawn rule.</summary>
    public enum SpawnRuleChange
    {
        Added,
        Updated,
        Removed,
    }

    /// <summary>
    /// Published after an admin adds, updates or removes a room spawn rule (<c>spawnrule</c>, and the
    /// count-1 rule <c>mkmob</c>/<c>mkitem</c> author). Audit-only. The rule fields carry the rule
    /// as it now stands (as it stood, for <see cref="SpawnRuleChange.Removed"/>).
    /// </summary>
    public sealed record SpawnRuleAuthoredByAdminEvent(
        uint AdminEntityId,
        uint RoomEntityId,
        string RoomBlueprintId,
        string RuleId,
        SpawnRuleChange Change,
        string? BlueprintId,
        int? Count,
        int? RespawnDelaySeconds) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
