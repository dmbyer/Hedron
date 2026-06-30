using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Shopping.Handlers
{
    /// <summary>
    /// Narrates buy and sell transactions to the room. Pure output fan-out — no gameplay logic,
    /// no persistence calls (INV-5). Priority Notification (80) — runs after
    /// <c>ItemContextHandler</c> (Domain = 20) has applied the persistence-pool transition.
    ///
    /// <para>
    /// Also responsible for removing <c>ShopStockComponent</c> from purchased items on buy —
    /// this domain-state cleanup is co-located with narration rather than in <c>ItemContextHandler</c>
    /// because <c>ItemContextHandler</c> already handles <see cref="ItemBoughtEvent"/> for
    /// persistence (domain cleanup in separate subscriber avoids over-loading one handler).
    /// </para>
    ///
    /// <para>
    /// See <see cref="Hedron.Core.Modules.Economy.Handlers.CurrencyAwardNarrationHandler"/> for the
    /// precedent narration pattern.
    /// </para>
    /// </summary>
    public sealed class ShopInteractionHandler :
        IEventHandler<ItemBoughtEvent>,
        IEventHandler<ItemSoldEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;
        private readonly ICurrencyRegistry _currencyRegistry;

        public int Priority => HandlerPriority.Notification;

        public ShopInteractionHandler(
            EntityService entityService,
            IBroadcastSystem broadcast,
            ICurrencyRegistry currencyRegistry)
        {
            _entityService = entityService;
            _broadcast = broadcast;
            _currencyRegistry = currencyRegistry;
        }

        public async Task HandleAsync(ItemBoughtEvent @event)
        {
            var playerName = GetPlayerName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);
            var priceStr = CurrencyFormatter.FormatAmount(@event.PricePaid, @event.Currency, _currencyRegistry);

            // Narrate to the buyer.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"You buy {itemName} for {priceStr}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);

            // Narrate to bystanders.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{playerName} buys {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(ItemSoldEvent @event)
        {
            var playerName = GetPlayerName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);
            var priceStr = CurrencyFormatter.FormatAmount(@event.PriceReceived, @event.Currency, _currencyRegistry);

            // Narrate to the seller.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"You sell {itemName} for {priceStr}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);

            // Narrate to bystanders.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{playerName} sells {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private string GetPlayerName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private string GetItemName(uint entityId) =>
            _entityService.TryGet<ItemDataComponent>(entityId, out var d) ? d.Name : "something";
    }
}
