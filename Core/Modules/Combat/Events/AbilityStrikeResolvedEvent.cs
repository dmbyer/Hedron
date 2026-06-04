using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Combat.Events
{
    /// <summary>
    /// Thin past-tense fact published unconditionally by an invocation command after calling
    /// <see cref="Systems.ICombatSystem.ResolveAbilityStrike"/>.
    /// <see cref="Handlers.AbilityStrikeHandler"/> reads this to render a fused narrative line
    /// and conditionally publish <see cref="CombatEndedEvent"/> for terminal outcomes.
    /// </summary>
    public sealed record AbilityStrikeResolvedEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        uint RoomEntityId,
        CombatRoundResult Result,
        string AbilityId,
        string? DefenderName) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
