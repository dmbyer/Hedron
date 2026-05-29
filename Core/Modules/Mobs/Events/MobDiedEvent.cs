using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Mobs.Events
{
    /// <summary>
    /// Published by <c>CombatMobDeathHandler</c> immediately before the mob entity is destroyed,
    /// so systems that need to inspect entity state (e.g. <c>SpawnSystem</c>) can act while the
    /// entity is still live.
    /// </summary>
    public sealed record MobDiedEvent(
        uint MobEntityId,
        string BlueprintId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
