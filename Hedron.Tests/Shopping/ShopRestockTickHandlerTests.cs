using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Handlers;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 2 handler tests for <see cref="ShopRestockTickHandler"/>.
    ///
    /// Coverage contract (shopping.md Test plan — handler tier):
    ///   • Forced tick at interval spawns exactly the shortfall; each fresh entity carries
    ///     <see cref="ShopStockComponent"/>&#160;<c>{ Provenance = Base }</c>.
    ///   • Sub-interval tick is a no-op — no entities spawned.
    ///   • Fully-stocked shop produces no spawn even at interval boundary.
    ///   • Multiple shops are each restocked independently.
    /// </summary>
    public sealed class ShopRestockTickHandlerTests
    {
        // ── Minimal entity template ───────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IEntityTemplate"/> stub that adds an <see cref="ItemDataComponent"/>
        /// so spawned items are recognizable as items in tests.
        /// </summary>
        private sealed class MinimalItemTemplate : IEntityTemplate
        {
            public string BlueprintId { get; }
            private readonly string _name;

            public MinimalItemTemplate(string blueprintId, string name = "item")
            {
                BlueprintId = blueprintId;
                _name = name;
            }

            public void Apply(Entity entity, EntityService entityService)
            {
                entityService.AddComponent(entity.Id, new ItemDataComponent { Name = _name, Value = 100 });
            }
        }

        // ── Fixture ───────────────────────────────────────────────────────────────

        private static readonly ShopOptions DefaultOptions = new()
        {
            RestockInterval = TimeSpan.FromMinutes(5),
            BuyBackRetention = TimeSpan.FromHours(1),
            BuyRatio = 2.0m,
            SellRatio = 0.5m,
            DefaultTillSeed = 100_000,
        };

        private static (
            ShopRestockTickHandler handler,
            EntityService ecs,
            TemplateRegistry registry,
            FakeClock clock)
            Build(ShopOptions? options = null)
        {
            var ecs = new EntityService();
            var clock = new FakeClock();
            var opt = options ?? DefaultOptions;
            var walletSystem = new WalletSystem(ecs);
            var itemSystem = new ItemSystem(ecs);
            var shopSystem = new ShopSystem(ecs, walletSystem, itemSystem, clock, Options.Create(opt));
            var registry = new TemplateRegistry(ecs);

            var handler = new ShopRestockTickHandler(ecs, shopSystem, registry, Options.Create(opt));
            return (handler, ecs, registry, clock);
        }

        /// <summary>Creates a shopkeeper entity with ShopComponent and InventoryComponent.</summary>
        private static uint MakeShop(EntityService ecs, ShopStockRow? stockRow = null)
        {
            var shop = ecs.CreateEntity();
            var shopComp = new ShopComponent { AcceptedCurrency = CurrencyId.Coin };
            if (stockRow != null)
                shopComp.BaseStock.Add(stockRow);
            ecs.AddComponent(shop.Id, shopComp);
            ecs.AddComponent(shop.Id, new InventoryComponent());
            return shop.Id;
        }

        /// <summary>Adds a base-stock item entity to the given shop's inventory.</summary>
        private static uint AddBaseItem(EntityService ecs, uint shopId, string blueprintId)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "sword", Value = 100 });
            ecs.AddComponent(item.Id, new BlueprintComponent { BlueprintId = blueprintId });
            ecs.AddComponent(item.Id, new ShopStockComponent { Provenance = StockProvenance.Base });
            ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        /// <summary>Builds a HeartbeatTickEvent whose Elapsed equals <paramref name="elapsed"/>.</summary>
        private static HeartbeatTickEvent Tick(long id, TimeSpan elapsed)
            => new HeartbeatTickEvent(TickId: id, Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(id), Elapsed: elapsed);

        // ── Tests ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task At_interval_spawns_exactly_the_shortfall_count()
        {
            // Authored: 3 swords; live: 1 — shortfall = 2.
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.sword", new MinimalItemTemplate("item.sword", "sword"));

            var shopId = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.sword", Quantity = 3 });
            AddBaseItem(ecs, shopId, "item.sword"); // 1 live

            var inventoryBefore = ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Count;

            // Fire one tick equal to the full interval.
            await handler.HandleAsync(Tick(1, DefaultOptions.RestockInterval));

            var inventoryAfter = ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Count;
            Assert.Equal(2, inventoryAfter - inventoryBefore); // exactly the shortfall
        }

        [Fact]
        public async Task Spawned_items_carry_ShopStockComponent_Base()
        {
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.potion", new MinimalItemTemplate("item.potion", "potion"));

            var shopId = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.potion", Quantity = 2 });
            // 0 live items — shortfall = 2.

            await handler.HandleAsync(Tick(1, DefaultOptions.RestockInterval));

            // All items in the inventory should carry ShopStockComponent { Base }.
            var inventory = ecs.Get<InventoryComponent>(shopId).ItemEntityIds;
            Assert.Equal(2, inventory.Count);
            foreach (var itemId in inventory)
            {
                Assert.True(ecs.HasComponent<ShopStockComponent>(itemId),
                    $"Entity {itemId} should carry ShopStockComponent.");
                var stock = ecs.Get<ShopStockComponent>(itemId);
                Assert.Equal(StockProvenance.Base, stock.Provenance);
            }
        }

        [Fact]
        public async Task Sub_interval_tick_is_a_no_op()
        {
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.helm", new MinimalItemTemplate("item.helm", "helm"));

            var shopId = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.helm", Quantity = 2 });
            // 0 live items — would be shortfall = 2 at interval.

            // Fire a tick that is less than the full interval.
            var subInterval = TimeSpan.FromSeconds(DefaultOptions.RestockInterval.TotalSeconds / 2);
            await handler.HandleAsync(Tick(1, subInterval));

            var inventory = ecs.Get<InventoryComponent>(shopId).ItemEntityIds;
            Assert.Empty(inventory); // no spawn until interval elapses
        }

        [Fact]
        public async Task Fully_stocked_shop_spawns_nothing_at_interval()
        {
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.shield", new MinimalItemTemplate("item.shield", "shield"));

            var shopId = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.shield", Quantity = 2 });
            AddBaseItem(ecs, shopId, "item.shield");
            AddBaseItem(ecs, shopId, "item.shield"); // fully stocked

            var countBefore = ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Count;

            await handler.HandleAsync(Tick(1, DefaultOptions.RestockInterval));

            var countAfter = ecs.Get<InventoryComponent>(shopId).ItemEntityIds.Count;
            Assert.Equal(countBefore, countAfter); // no new entities
        }

        [Fact]
        public async Task Accumulated_sub_interval_ticks_trigger_at_boundary()
        {
            // Two ticks each of RestockInterval/2 should trigger on the second tick.
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.ring", new MinimalItemTemplate("item.ring", "ring"));

            var shopId = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.ring", Quantity = 1 });
            var halfInterval = TimeSpan.FromMilliseconds(DefaultOptions.RestockInterval.TotalMilliseconds / 2);

            await handler.HandleAsync(Tick(1, halfInterval));
            Assert.Empty(ecs.Get<InventoryComponent>(shopId).ItemEntityIds); // not yet

            await handler.HandleAsync(Tick(2, halfInterval));
            Assert.Single(ecs.Get<InventoryComponent>(shopId).ItemEntityIds); // triggered
        }

        [Fact]
        public async Task Multiple_shops_are_each_restocked_independently()
        {
            var (handler, ecs, registry, _) = Build();
            registry.Register("item.a", new MinimalItemTemplate("item.a", "itemA"));
            registry.Register("item.b", new MinimalItemTemplate("item.b", "itemB"));

            var shopA = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.a", Quantity = 2 });
            var shopB = MakeShop(ecs, new ShopStockRow { BlueprintId = "item.b", Quantity = 1 });
            // Both shops start empty.

            await handler.HandleAsync(Tick(1, DefaultOptions.RestockInterval));

            Assert.Equal(2, ecs.Get<InventoryComponent>(shopA).ItemEntityIds.Count);
            Assert.Single(ecs.Get<InventoryComponent>(shopB).ItemEntityIds);
        }
    }
}
