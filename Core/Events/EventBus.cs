using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Events
{
    /// <summary>
    /// In-memory <see cref="IEventBus"/> implementation.
    /// </summary>
    /// <remarks>
    /// Subscribers are kept sorted by <see cref="IEventHandler{TEvent}.Priority"/>. A
    /// publish takes a snapshot of the subscriber list under lock and dispatches outside
    /// the lock so handlers can subscribe/unsubscribe as a side effect without deadlock.
    /// Not yet tuned for high-throughput contention — see the Phase 4 hardening pass.
    /// </remarks>
    public class EventBus : IEventBus
    {
        private readonly Dictionary<Type, List<object>> _handlers = new();
        private readonly object _lock = new();

        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list = new List<object>();
                    _handlers[typeof(TEvent)] = list;
                }

                list.Add(handler);
                list.Sort((a, b) => ((IEventHandler<TEvent>)a).Priority
                    .CompareTo(((IEventHandler<TEvent>)b).Priority));
            }
        }

        public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            lock (_lock)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                    list.Remove(handler);
            }
        }

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            if (@event is null) throw new ArgumentNullException(nameof(@event));

            IEventHandler<TEvent>[] snapshot;
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
                    return;

                snapshot = list.Cast<IEventHandler<TEvent>>().ToArray();
            }

            foreach (var handler in snapshot)
                await handler.HandleAsync(@event).ConfigureAwait(false);
        }

        public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
        {
            PublishAsync(@event).GetAwaiter().GetResult();
        }
    }
}
