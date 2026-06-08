using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Events;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Test double for <see cref="IEventBus"/>.
    /// Appends every published event to <see cref="Published"/>.
    /// When constructed with <c>dispatch: true</c>, also invokes subscribed handlers in
    /// <see cref="IEventHandler{TEvent}.Priority"/> order.
    /// </summary>
    public sealed class RecordingEventBus : IEventBus
    {
        private readonly bool _dispatch;
        private readonly Dictionary<Type, List<object>> _handlers = new();
        private readonly List<IEvent> _published = new();
        private readonly object _lock = new();

        public RecordingEventBus(bool dispatch = false)
        {
            _dispatch = dispatch;
        }

        /// <summary>All events published through this bus, in publication order.</summary>
        public IReadOnlyList<IEvent> Published
        {
            get { lock (_lock) return _published.ToList().AsReadOnly(); }
        }

        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                    _handlers[typeof(TEvent)] = list = new List<object>();
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

        public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
            => PublishAsync(@event).GetAwaiter().GetResult();

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            if (@event is null) throw new ArgumentNullException(nameof(@event));

            lock (_lock) _published.Add(@event);

            if (!_dispatch) return;

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
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class RecordingEventBusTests
    {
        private sealed record TestEvent : IEvent
        {
            public int Value { get; init; }
            public DateTime OccurredAt { get; } = DateTime.UtcNow;
            public Guid EventId { get; } = Guid.NewGuid();
        }

        private sealed class OrderTracker
        {
            public List<int> Order { get; } = new();
        }

        private sealed class TrackingHandler : IEventHandler<TestEvent>
        {
            private readonly OrderTracker _tracker;
            public int Priority { get; }
            public TrackingHandler(OrderTracker tracker, int priority) { _tracker = tracker; Priority = priority; }
            public Task HandleAsync(TestEvent @event) { _tracker.Order.Add(Priority); return Task.CompletedTask; }
        }

        [Fact]
        public void Publish_appends_event_to_Published()
        {
            var bus = new RecordingEventBus();
            bus.Publish(new TestEvent { Value = 42 });
            Assert.Single(bus.Published);
            Assert.Equal(42, ((TestEvent)bus.Published[0]).Value);
        }

        [Fact]
        public async Task PublishAsync_with_dispatch_invokes_handlers_in_priority_order()
        {
            var tracker = new OrderTracker();
            var bus = new RecordingEventBus(dispatch: true);
            bus.Subscribe(new TrackingHandler(tracker, priority: 20));
            bus.Subscribe(new TrackingHandler(tracker, priority: 10));
            bus.Subscribe(new TrackingHandler(tracker, priority: 30));

            await bus.PublishAsync(new TestEvent());

            Assert.Equal(new[] { 10, 20, 30 }, tracker.Order);
        }

        [Fact]
        public async Task PublishAsync_without_dispatch_does_not_invoke_handlers()
        {
            var tracker = new OrderTracker();
            var bus = new RecordingEventBus(dispatch: false);
            bus.Subscribe(new TrackingHandler(tracker, priority: 10));

            await bus.PublishAsync(new TestEvent());

            Assert.Empty(tracker.Order);
            Assert.Single(bus.Published);
        }
    }
}
