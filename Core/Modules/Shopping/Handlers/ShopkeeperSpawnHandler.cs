using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Shopping.Handlers
{
    /// <summary>
    /// Subscribes to <see cref="WorldContentReadyEvent"/> (priority Domain = 20) and seeds every
    /// shopkeeper mob that was spawned from a <see cref="ShopComponent"/>-carrying template:
    ///
    /// <list type="bullet">
    ///   <item>Deposits the configured till amount into the shopkeeper's <c>WalletComponent</c>
    ///         (from <see cref="ShopComponent.TillSeed"/>, or <see cref="ShopOptions.DefaultTillSeed"/>
    ///         when the per-shop seed is 0).</item>
    ///   <item>Spawns the authored base-stock items into the shopkeeper's <c>InventoryComponent</c>,
    ///         each stamped with <see cref="ShopStockComponent"/>&#160;<c>{ Provenance = Base }</c>.</item>
    /// </list>
    ///
    /// <para>
    /// <b>No <c>PersistentEntity</c></b> is added to the shopkeeper or to base-stock items —
    /// both are world content that re-spawns fresh on every startup (INV-23). The
    /// <c>WalletComponent</c> on the shopkeeper is <c>[Persistent]</c>-tagged but is never
    /// written because the mob carries no <c>PersistentEntity</c> (two-level opt-in, INV-14).
    /// </para>
    ///
    /// <para>
    /// This handler is analogous to <c>SpawnSystem.HandleAsync(WorldContentReadyEvent)</c> and
    /// <c>WorldContentLoader.PlaceItemsInRooms</c>: it performs the second pass of shop setup
    /// that requires services unavailable in <c>MobTemplate.Apply</c> (<c>ITemplateRegistry</c>,
    /// <c>IShopSystem</c> — which owns the till-seeding rule + <c>ShopOptions</c>).
    /// </para>
    /// </summary>
    public sealed class ShopkeeperSpawnHandler : IEventHandler<WorldContentReadyEvent>
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IShopSystem _shopSystem;
        private readonly ILogger<ShopkeeperSpawnHandler> _logger;

        public int Priority => HandlerPriority.Domain;

        public ShopkeeperSpawnHandler(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IShopSystem shopSystem,
            ILogger<ShopkeeperSpawnHandler> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _shopSystem = shopSystem;
            _logger = logger;
        }

        public Task HandleAsync(WorldContentReadyEvent @event)
        {
            foreach (var (shopEntityId, shop) in _entityService.GetAllComponents<ShopComponent>())
            {
                // Till seeding rule lives in the system (INV-8); the handler orchestrates only.
                _shopSystem.SeedTill(shopEntityId);
                SpawnBaseStock(shopEntityId, shop);
            }

            return Task.CompletedTask;
        }

        // ── Base-stock spawning ──────────────────────────────────────────────────

        private void SpawnBaseStock(uint shopEntityId, ShopComponent shop)
        {
            if (shop.BaseStock.Count == 0)
                return;

            // Ensure the shopkeeper has an inventory (Apply should have added one, but guard anyway).
            if (!_entityService.TryGet<InventoryComponent>(shopEntityId, out var inventory))
            {
                inventory = new InventoryComponent();
                _entityService.AddComponent(shopEntityId, inventory);
            }

            foreach (var row in shop.BaseStock)
            {
                if (string.IsNullOrEmpty(row.BlueprintId))
                    continue;

                if (!_templateRegistry.TryGet(row.BlueprintId, out _))
                {
                    _logger.LogWarning(
                        "ShopkeeperSpawnHandler: shop entity={ShopId} base-stock blueprint '{BlueprintId}' not found — skipping.",
                        shopEntityId, row.BlueprintId);
                    continue;
                }

                var quantity = row.Quantity > 0 ? row.Quantity : 1;
                for (var i = 0; i < quantity; i++)
                {
                    var itemEntity = _templateRegistry.Spawn(row.BlueprintId);

                    // Stamp with ShopStockComponent { Base } — this is the provenance marker.
                    // NOT [Persistent]; world-transient content (INV-23).
                    _entityService.AddComponent(itemEntity.Id, new ShopStockComponent
                    {
                        Provenance = StockProvenance.Base,
                        ExpiresAt = null,
                    });

                    // Add to shopkeeper's inventory (no LocationComponent — shop items are in
                    // inventory, not on the ground).
                    inventory.ItemEntityIds.Add(itemEntity.Id);

                    _logger.LogDebug(
                        "ShopkeeperSpawnHandler: spawned base-stock entity={ItemId} blueprint='{BlueprintId}' into shop={ShopId}",
                        itemEntity.Id, row.BlueprintId, shopEntityId);
                }
            }
        }
    }
}
