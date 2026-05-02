using Hedron.Core.Events;
using Hedron.Core.Systems;

namespace Hedron.Core.Handlers
{
    /// <summary>
    /// Marks entities dirty whenever a state-change event mutates <c>[Persistent]</c> data.
    /// </summary>
    /// <remarks>
    /// At this slice's scope (Phase 3 slice 1) no MVP component carries <c>[Persistent]</c>,
    /// so no events are subscribed yet. The handler is wired here as a no-op skeleton;
    /// subsequent slices add their events by implementing
    /// <see cref="IEventHandler{TEvent}"/> on this class and subscribing in
    /// <c>PersistenceModule</c>.
    /// <para>
    /// Priority 90 on all subscribed events — dirty-marking runs after domain processing
    /// (priority 50) but before any late-processing steps.
    /// </para>
    /// </remarks>
    public sealed class PersistenceHandler
    {
        private readonly IPersistenceSystem _persistence;

        public PersistenceHandler(IPersistenceSystem persistence)
        {
            _persistence = persistence;
        }

        // Future event subscriptions are added here when slices introduce [Persistent] data.
        // Example pattern for an upcoming slice:
        //
        //   public sealed class OnPlayerMoved : IEventHandler<PlayerMovedEvent>
        //   {
        //       private readonly PersistenceHandler _parent;
        //       public OnPlayerMoved(PersistenceHandler parent) => _parent = parent;
        //       public int Priority => 90;
        //       public Task HandleAsync(PlayerMovedEvent e)
        //       {
        //           _parent._persistence.MarkDirty(e.PlayerEntityId);
        //           return Task.CompletedTask;
        //       }
        //   }
    }
}
