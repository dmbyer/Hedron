using System.Threading.Tasks;

namespace Hedron.Core.Events
{
    /// <summary>
    /// Handles a specific event type. Handlers orchestrate — they call domain systems and
    /// publish follow-on events but contain no game logic themselves.
    /// </summary>
    /// <remarks>
    /// Priorities tie-break within one event type — lower runs first. See
    /// <see cref="HandlerPriority"/> for canonical tiers and
    /// <c>docs/architecture/03-events.md</c> for when to use phased events vs. priorities.
    /// </remarks>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        Task HandleAsync(TEvent @event);

        /// <summary>Lower values dispatch earlier within the same event type.</summary>
        int Priority { get; }
    }
}
