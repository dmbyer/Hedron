using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Economy.Handlers
{
    /// <summary>
    /// Writes a "You receive …" narrative line to the recipient after currency is awarded.
    /// Pure presentation — no domain logic, no persistence calls.
    /// Priority 80 (<see cref="HandlerPriority.Notification"/>).
    ///
    /// <para>
    /// Uses <see cref="CurrencyFormatter"/> (shared with <c>TelnetOutputFormatter</c>) to
    /// render the amount up the denomination ladder via <see cref="ICurrencyRegistry"/>.
    /// </para>
    /// </summary>
    public sealed class CurrencyAwardNarrationHandler : IEventHandler<CurrencyAwardedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;
        private readonly ICurrencyRegistry _currencyRegistry;

        public int Priority => HandlerPriority.Notification;

        public CurrencyAwardNarrationHandler(
            EntityService entityService,
            IBroadcastSystem broadcast,
            ICurrencyRegistry currencyRegistry)
        {
            _entityService = entityService;
            _broadcast = broadcast;
            _currencyRegistry = currencyRegistry;
        }

        public async Task HandleAsync(CurrencyAwardedEvent @event)
        {
            // Resolve the recipient's current room so we can use the room-based broadcast.
            // If the player has left the room or the event fires in an edge case with no location,
            // we silently skip the narration rather than erroring.
            if (!_entityService.TryGet<LocationComponent>(@event.RecipientEntityId, out var loc))
                return;

            var message = FormatAwardMessage(@event.Amount, @event.Currency, _currencyRegistry);

            await _broadcast.SendToRoomAsync(
                loc!.RoomEntityId,
                new PlainMessage(message, OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.RecipientEntityId)
                .ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Formats the award message using the shared <see cref="CurrencyFormatter"/> ladder
        /// rendering so the "You receive …" line shows full denomination words (e.g. "1 gold, 0 silver, 5 copper").
        /// </summary>
        public static string FormatAwardMessage(long amount, CurrencyId currency, ICurrencyRegistry registry)
            => $"You receive {CurrencyFormatter.FormatAmount(amount, currency, registry)}.";
    }
}
