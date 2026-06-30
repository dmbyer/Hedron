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
using Hedron.Core.Modules.Shopping.Events;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Shopping.Commands
{
    /// <summary>
    /// Player verb <c>buy &lt;item&gt;</c>. Buys an item from the shopkeeper in the room.
    /// Works for both base-stock items and buy-back-shelf items (resolved decision 5 — same verb).
    ///
    /// <para>
    /// Thin (INV-8): resolves shopkeeper + item → calls <see cref="IShopSystem.TryResolveBuy"/>
    /// → on success calls <see cref="IWalletSystem.Transfer"/> + <see cref="IItemSystem.MoveBetweenInventories"/>
    /// → publishes <see cref="ItemBoughtEvent"/>. All gameplay rules (pricing, affordability) are
    /// in <see cref="IShopSystem"/>.
    /// </para>
    ///
    /// <para>
    /// The shopkeeper is the implicit <see cref="ShopComponent"/>-bearing mob in the room
    /// (no named argument), so this command resolves it directly rather than through an
    /// <c>IArgumentResolver</c>; only the item token is a player-supplied argument.
    /// </para>
    /// </summary>
    public sealed class BuyCommand : ICommand
    {
        private readonly IShopSystem _shopSystem;
        private readonly IItemSystem _itemSystem;
        private readonly IWalletSystem _walletSystem;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "buy";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Buy an item from a shopkeeper.";
        public string LongDescription =>
            "Buys a named item from a shopkeeper in the current room. " +
            "Also works for buy-back items you previously sold.";
        public string Usage => "buy <item>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();

        public CommandArgumentSchema ArgumentSchema { get; }

        public BuyCommand(
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
                    Required: true, "Name or keyword of the item to buy."),
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
                    new PlainMessage("Buy what?", OutputSeverity.System, OutputCategory.System))
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

            // Resolve the item from the shopkeeper's inventory.
            if (!_itemSystem.TryFindItemInInventory(shopEntityId, itemToken!, out var itemEntityId))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("The shopkeeper does not have that.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Validate the purchase (pricing, affordability) — pure, no mutation.
            var result = _shopSystem.TryResolveBuy(context.InvokerEntityId, shopEntityId, itemEntityId);
            if (!result.Success)
            {
                await context.Output.WriteAsync(
                    new PlainMessage(result.FailureReason ?? "You cannot buy that.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            // Execute: transfer funds, move item, publish event.
            // INV-8: transfer + move are the Initiator's responsibility after the system approves.
            _walletSystem.Transfer(context.InvokerEntityId, shopEntityId, result.Currency, result.Price);
            _itemSystem.MoveBetweenInventories(itemEntityId, shopEntityId, context.InvokerEntityId);

            await _eventBus.PublishAsync(new ItemBoughtEvent(
                context.InvokerEntityId, shopEntityId, itemEntityId, roomEntityId,
                result.Price, result.Currency))
                .ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private uint FindShopkeeperInRoom(uint roomEntityId)
        {
            foreach (var (entityId, _) in _entityService.GetAllComponents<Hedron.Core.Modules.Shopping.Components.ShopComponent>())
            {
                if (_entityService.TryGet<LocationComponent>(entityId, out var mobLoc)
                    && mobLoc!.RoomEntityId == roomEntityId)
                    return entityId;
            }
            return 0u;
        }
    }
}
