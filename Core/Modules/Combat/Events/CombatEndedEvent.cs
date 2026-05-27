using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Combat.Events
{
    public enum CombatEndOutcome { MobDied, PlayerIncapacitated, PlayerFled }

    public sealed record CombatEndedEvent(
        uint AttackerEntityId,
        uint DefenderEntityId,
        CombatEndOutcome Outcome,
        uint RoomEntityId,
        string? DefenderName = null) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
