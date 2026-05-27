using Hedron.Core.ECS.Components;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.EntityState.Events
{
    public sealed record EntityStateChangedEvent(
        uint EntityId,
        EntityStateFlags OldStates,
        EntityStateFlags NewStates) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
