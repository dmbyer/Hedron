using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Progression.Events
{
    /// <summary>
    /// A player sized up a mob with <c>consider</c> and saw <see cref="DangerLabel"/>. A thin
    /// observation fact, published by <c>ConsiderCommand</c> (Initiator) only when a label was
    /// actually shown — never on a refusal, a not-found, an untargetable target or an unreadable
    /// reading. Carries ids and the label only, never power values (INV-6). No gameplay handler
    /// subscribes; the playtest recorder is its intended consumer.
    /// <paramref name="MobBlueprintId"/> is the mob's <c>BlueprintComponent</c> id, or
    /// <see cref="string.Empty"/> when it has none.
    /// </summary>
    public sealed record MobConsideredEvent(
        uint PlayerEntityId,
        string MobBlueprintId,
        RelativePowerLabel DangerLabel) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
