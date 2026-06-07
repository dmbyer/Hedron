using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.Modules.Combat.Events
{
    /// <summary>
    /// Thin past-tense fact published unconditionally by an invocation command after calling
    /// <see cref="Systems.ICombatSystem.ResolveAbilityStrike"/>.
    /// <see cref="Handlers.AbilityStrikeHandler"/> reads this to render a fused narrative line
    /// and conditionally publish <see cref="CombatEndedEvent"/> for terminal outcomes.
    /// <para>
    /// <see cref="AspectComposition"/> is a point-in-time capture of the damage typing from the
    /// ability's <c>Aspect</c> field at strike time — null when the ability was untyped (INV-6).
    /// </para>
    /// </summary>
    public sealed record AbilityStrikeResolvedEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        uint RoomEntityId,
        CombatRoundResult Result,
        string AbilityId,
        string? DefenderName,
        AspectComposition? AspectComposition = null) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
