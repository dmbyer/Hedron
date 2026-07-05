using System;
using Hedron.Core.Events;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression.Events
{
    /// <summary>
    /// A track gained XP. Thin and frequent (prompt/telemetry consumers) — published once per
    /// awarding action that produced a positive amount, never for a no-op award.
    /// </summary>
    public sealed record ExperienceAwardedEvent(
        uint EntityId,
        ScoreId Track,
        int Amount,
        XpSource Source) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
