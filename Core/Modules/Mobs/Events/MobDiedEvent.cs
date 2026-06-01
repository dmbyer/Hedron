using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Mobs.Events
{
    /// <summary>
    /// Published by <c>CombatMobDeathHandler</c> immediately before the mob entity is destroyed,
    /// so systems that need to inspect entity state (e.g. <c>SpawnSystem</c>) can act while the
    /// entity is still live.
    /// <para><c>KillerEntityId == 0</c> is the "no attributable killer" sentinel.</para>
    /// </summary>
    public sealed record MobDiedEvent(
        uint MobEntityId,
        string BlueprintId,
        uint KillerEntityId = 0) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
