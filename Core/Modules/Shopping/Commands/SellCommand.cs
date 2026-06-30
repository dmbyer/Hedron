using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Shopping.Commands
{
    /// <summary>
    /// Player verb <c>sell &lt;item&gt;</c>. Sells an item from the player's inventory to the
    /// shopkeeper in the room.
    ///
    /// <para>
    /// Thin (INV-8): resolves shopkeeper + item → calls <see cref="IShopSystem.TryResolveSell"/>
    /// (which computes price, validates till affordability, and returns the clock-derived
    /// <c>ExpiresAt</c>) → on success calls <see cref="IWalletSystem.Transfer"/> +
    /// <see cref="IItemSystem.MoveBetweenInventories"/> → stamps <see cref="ShopStockComponent"/>
    /// with the sell-returned <c>ExpiresAt</c> (INV-8) → publishes <see cref="ItemSoldEvent"/>.
    /// </para>
    ///
    /// <para>
    /// The shopkeeper is the implicit <see cref="ShopComponent"/>-bearing mob in the room
    /// (no named argument), so this command resolves it directly rather than through an
    /// <c>IArgumentResolver</c>; only the item token is a player-supplied argument.
    /// </para>
    /// </summary>
    public sealed class SellCommand : ICommand
    {
        private readonly IShopSystem _shopSystem;
        private readonly IItemSystem _itemSystem;
        private readonly IWalletSystem _walletSystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "sell";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Sell an item to a shopkeeper.";
        public string LongDescription =>
            "Sells a named item from your inventory to a shopkeeper in the current room.";
        public string Usage => "sell <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();

        public CommandArgumentSchema ArgumentSchema { get; }

        public SellCommand(
            IShopSystem shopSystem,
            IItemSystem itemSystem,
            IWalletSystem walletSystem,
            EntityService entityService,
            IEventBus eventBus)
        {
            _shopSystem = shopSystem;
            _itemSystem = itemSystem;
            _walletSystem = walletSystem;
            _entityService = entityService;
            _eventBus = eventBus;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("item", typeof(string), CommandArgumentKind.Token,
                    Required: true, "Name or keyword of the item to sell."),
            });
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var roomEntityId = location!.RoomEntityId;

            if (!context.Args.TryGet<string>("item", out var itemToken) || string.IsNullOrWhiteSpace(itemToken))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("Sell what?", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Resolve the shopkeeper — first ShopComponent mob in the room.
            var shopEntityId = FindShopkeeperInRoom(roomEntityId);
            if (shopEntityId == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("There is no shopkeeper here.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Resolve the item from the player's inventory.
            if (!_itemSystem.TryFindItemInInventory(context.InvokerEntityId, itemToken!, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You aren't carrying that.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Validate the sale (value > 0, till affordability, compute price + ExpiresAt) — pure.
            var result = _shopSystem.TryResolveSell(context.InvokerEntityId, shopEntityId, itemEntityId);
            if (!result.Success)
            {
                await context.Output.WriteAsync(
                    new PlainMessage(result.FailureReason ?? "The shopkeeper refuses.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Execute: transfer funds, move item.
            _walletSystem.Transfer(shopEntityId, context.InvokerEntityId, result.Currency, result.Price);
            _itemSystem.MoveBetweenInventories(itemEntityId, context.InvokerEntityId, shopEntityId);

            // Stamp ShopStockComponent with the ExpiresAt returned by the system (INV-8: arithmetic
            // lived in TryResolveSell; the command stamps the result).
            _entityService.AddComponent(itemEntityId, new ShopStockComponent
            {
                Provenance = StockProvenance.Acquired,
                ExpiresAt = result.ExpiresAt,
            });

            await _eventBus.PublishAsync(new ItemSoldEvent(
                context.InvokerEntityId, shopEntityId, itemEntityId, roomEntityId,
                result.Price, result.Currency))
                .ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private uint FindShopkeeperInRoom(uint roomEntityId)
        {
            foreach (var (entityId, _) in _entityService.GetAllComponents<ShopComponent>())
            {
                if (_entityService.TryGet<LocationComponent>(entityId, out var mobLoc)
                    && mobLoc!.RoomEntityId == roomEntityId)
                    return entityId;
            }
            return 0u;
        }
    }
}
