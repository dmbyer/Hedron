using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.Modules.Combat.Events
{
    /// <summary>
    /// Point-in-time past-tense fact published by <see cref="Handlers.CombatTickHandler"/>
    /// after each melee round completes. <see cref="AspectComposition"/> is captured at
    /// publish time (INV-6) — null when the strike was untyped.
    /// </summary>
    public sealed record CombatRoundEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        uint RoomEntityId,
        CombatRoundResult Result,
        AspectComposition? AspectComposition = null) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
