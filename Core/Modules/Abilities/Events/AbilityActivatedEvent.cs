using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Abilities.Events
{
    /// <summary>
    /// An ability activated successfully. <paramref name="TargetEntityId"/> is the resolved target
    /// (the actor's own id for a <c>Targeting.Self</c> ability, so a non-null target does not by
    /// itself mean an opponent). <paramref name="IsOffensive"/> is the publisher's fact that the use
    /// was offensive <b>against</b> <paramref name="TargetEntityId"/> (<c>IAbilitySystem.IsOffensive</c>
    /// with a target) — the one signal <c>AdvancementHandler</c> uses to name the target as the
    /// use's opponent for anti-grind. Thin flag, no power value (INV-6).
    /// </summary>
    public sealed record AbilityActivatedEvent(
        uint ActorEntityId,
        string AbilityId,
        uint? TargetEntityId,
        bool IsOffensive = false) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
