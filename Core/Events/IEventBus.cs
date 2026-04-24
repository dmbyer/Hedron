using System.Threading.Tasks;

namespace Hedron.Core.Events
{
    /// <summary>
    /// In-process event bus. Registered as a DI singleton in the composition root.
    /// </summary>
    /// <remarks>
    /// Per-handler dispatch order is determined by <see cref="IEventHandler{TEvent}.Priority"/>
    /// (lower first). See <c>docs/architecture/03-events.md</c>.
    /// </remarks>
    public interface IEventBus
    {
        /// <summary>
        /// Publishes an event and blocks until every subscribed handler has finished.
        /// Prefer <see cref="PublishAsync{TEvent}"/> from async call sites.
        /// </summary>
        void Publish<TEvent>(TEvent @event) where TEvent : IEvent;

        /// <summary>
        /// Publishes an event; each handler runs to completion in priority order.
        /// </summary>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;

        /// <summary>Registers a handler for events of type <typeparamref name="TEvent"/>.</summary>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;

        /// <summary>Removes a previously registered handler.</summary>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    }
}
