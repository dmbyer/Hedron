using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs.Resolvers;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Shopping.Commands
{
    /// <summary>
    /// Player verb <c>list</c>. Browses the shop's inventory — base stock and buy-back shelf
    /// together — showing each item's name and buy price. No state mutation.
    ///
    /// <para>
    /// Thin (INV-8): resolves the shopkeeper via <see cref="MobInRoomResolver"/>, calls
    /// <see cref="IShopSystem.GetListing"/>, and renders the listing to the invoker.
    /// The shopkeeper argument is auto-matched by <see cref="MobInRoomResolver"/> — if exactly
    /// one shopkeeper is in the room the argument may be omitted or "shop". Acquired rows are
    /// flagged as "(buy-back)" in the output.
    /// </para>
    ///
    /// <para>
    /// <b>Note on MobInRoomResolver placement:</b> the resolver lives in the shared
    /// <c>Core/Modules/Mobs/Resolvers/</c> home. <c>list</c> is currently its only active consumer
    /// (combat + ability targeting still use inline <c>ICombatSystem.TryFindTargetInRoom</c>; their
    /// migration to this resolver is backlogged). See resolved Q2 in shopping.md.
    /// </para>
    /// </summary>
    public sealed class ListCommand : ICommand
    {
        private readonly IShopSystem _shopSystem;
        private readonly EntityService _entityService;
        private readonly ICurrencyRegistry _currencyRegistry;
        private readonly MobInRoomResolver _mobResolver;

        public string Name => "list";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public string ShortDescription => "Browse a shop's wares.";
        public string LongDescription =>
            "Lists all items for sale at a shopkeeper in the room, including the buy-back shelf.";
        public string Usage => "list [shopkeeper]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();

        public CommandArgumentSchema ArgumentSchema { get; }

        public ListCommand(
            IShopSystem shopSystem,
            EntityService entityService,
            ICurrencyRegistry currencyRegistry,
            MobInRoomResolver mobResolver)
        {
            _shopSystem = shopSystem;
            _entityService = entityService;
            _currencyRegistry = currencyRegistry;
            _mobResolver = mobResolver;

            ArgumentSchema = new CommandArgumentSchema(new[]
            {
                new CommandArgument("shopkeeper", typeof(string), CommandArgumentKind.Token,
                    Required: false, "Name or keyword of the shopkeeper.", _mobResolver),
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

            var shopEntityId = ResolveShopkeeper(context, location!.RoomEntityId);
            if (shopEntityId == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("There is no shopkeeper here.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var listing = _shopSystem.GetListing(shopEntityId);

            if (listing.Rows.Count == 0)
            {
                await context.Output.WriteAsync(
                    new PlainMessage("The shop has nothing for sale.", OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Items for sale:");
            foreach (var row in listing.Rows)
            {
                var price = CurrencyFormatter.FormatAmount(row.BuyPrice, row.Currency, _currencyRegistry);
                var flag = row.IsAcquired ? " (buy-back)" : string.Empty;
                sb.AppendLine($"  {row.Name} — {price}{flag}");
            }

            await context.Output.WriteAsync(
                new PlainMessage(sb.ToString().TrimEnd(), OutputSeverity.System, OutputCategory.System))
                .ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the shopkeeper from the optional argument or falls back to the first
        /// shopkeeper in the room. Returns 0 if none found.
        /// </summary>
        private uint ResolveShopkeeper(CommandContext context, uint roomEntityId)
        {
            // If the argument resolved to a canonical entity id, use it.
            if (context.Args.TryGet<string>("shopkeeper", out var canonical)
                && !string.IsNullOrWhiteSpace(canonical)
                && uint.TryParse(canonical, out var parsed))
            {
                return _entityService.HasComponent<Hedron.Core.Modules.Shopping.Components.ShopComponent>(parsed)
                    ? parsed
                    : 0u;
            }

            // Fall back: find the first mob with ShopComponent in the room.
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
