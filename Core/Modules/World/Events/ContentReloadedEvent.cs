using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.World.Events
{
    /// <summary>
    /// Published after a successful <c>@reload</c> — content directory was re-scanned and
    /// the template registry refreshed. Existing live entities are not mutated.
    /// </summary>
    public record ContentReloadedEvent(
        int TemplatesLoaded,
        int TemplatesUnchanged,
        int TemplatesRemoved) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
