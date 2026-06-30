using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Shopping.Handlers;
using Hedron.Core.Modules.Shopping.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// WP-1 exit criterion tests (persistence round-trip + shopkeeper spawn assertions).
    ///
    /// Coverage contract (shopping.md Test plan — persistence round-trip):
    ///
    ///   • Shopkeeper authored in YAML spawns with a seeded till.
    ///   • Base-stock items carry <see cref="ShopStockComponent"/>&#160;<c>{ Provenance = Base }</c>.
    ///   • No <see cref="PersistentEntity"/> on shopkeeper or base-stock items (world content, INV-23).
    ///   • The <c>shop:</c> YAML block round-trips: acceptedCurrency, tillSeed, baseStock rows survive
    ///     write → YAML → read.
    ///   • Absent / null shop block → not a shopkeeper (opt-in default).
    ///   • Unknown currency name in YAML → defaults to Coin (log-and-default, no throw).
    ///
    /// Tests <see cref="MobContentWriter"/> (write) → <see cref="MobTemplateDeserializer"/> (read)
    /// → <see cref="MobTemplate.Apply"/> → <see cref="ShopkeeperSpawnHandler"/> (till seed + stock).
    /// </summary>
    public sealed class ShopkeeperRoundTripTests : IDisposable
    {
        private readonly string _tempDir;

        public ShopkeeperRoundTripTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"hedron-shop-roundtrip-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private MobContentWriter BuildWriter() =>
            new MobContentWriter(Options.Create(new WorldOptions { ContentDirectory = _tempDir }));

        private static MobTemplateDeserializer BuildDeserializer() =>
            new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance);

        private async Task<MobTemplate> RoundTrip(MobTemplate original)
        {
            await BuildWriter().WriteAsync(original);
            var yamlPath = Path.Combine(_tempDir, "mobs", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);
            return (MobTemplate)BuildDeserializer().Deserialize(yaml);
        }

        /// <summary>
        /// Builds a <see cref="TemplateRegistry"/> that contains a minimal item template so
        /// base-stock spawns can succeed, then spawns the given mob template and returns
        /// (shopEntityId, ecs, registry).
        /// </summary>
        private static (uint shopEntityId, EntityService ecs, TemplateRegistry registry)
            SpawnShopkeeper(MobTemplate mobTemplate, IEntityTemplate? stockItemTemplate = null)
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);

            // Register stock item template if provided.
            if (stockItemTemplate is not null)
                registry.Register(stockItemTemplate.BlueprintId, stockItemTemplate);

            // Register and spawn the mob template.
            registry.Register(mobTemplate.BlueprintId, mobTemplate);
            var shopEntity = registry.Spawn(mobTemplate.BlueprintId);

            return (shopEntity.Id, ecs, registry);
        }

        /// <summary>Runs <see cref="ShopkeeperSpawnHandler"/> synchronously against the given ecs/registry.</summary>
        private static async Task RunSpawnHandler(
            EntityService ecs, TemplateRegistry registry, ShopOptions? options = null)
        {
            // The handler delegates till-seeding to IShopSystem (INV-8); build a real ShopSystem.
            var shopSystem = new ShopSystem(
                ecs,
                new WalletSystem(ecs),
                new ItemSystem(ecs),
                new FakeClock(),
                Options.Create(options ?? new ShopOptions()));

            var handler = new ShopkeeperSpawnHandler(
                ecs,
                registry,
                shopSystem,
                NullLogger<ShopkeeperSpawnHandler>.Instance);

            await handler.HandleAsync(new Hedron.Core.Modules.World.Events.WorldContentReadyEvent());
        }

        // ── YAML round-trip: shop block survives write → read ─────────────────────

        [Fact]
        public async Task Shop_block_with_currency_and_tillSeed_survives_round_trip()
        {
            var original = new MobTemplate("mob.shopkeeper.yaml-rt")
            {
                Name = "Merchant",
                IsShop = true,
                ShopAcceptedCurrency = CurrencyId.Coin,
                ShopTillSeed = 50_000,
            };

            var loaded = await RoundTrip(original);

            Assert.True(loaded.IsShop);
            Assert.Equal(CurrencyId.Coin, loaded.ShopAcceptedCurrency);
            Assert.Equal(50_000, loaded.ShopTillSeed);
        }

        [Fact]
        public async Task Shop_base_stock_rows_survive_round_trip()
        {
            var original = new MobTemplate("mob.shopkeeper.stock-rt")
            {
                Name = "Armorer",
                IsShop = true,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.iron.sword", Quantity = 3 },
                    new() { BlueprintId = "item.leather.helm", Quantity = 2 },
                },
            };

            var loaded = await RoundTrip(original);

            Assert.True(loaded.IsShop);
            Assert.Equal(2, loaded.ShopBaseStock.Count);
            Assert.Equal("item.iron.sword", loaded.ShopBaseStock[0].BlueprintId);
            Assert.Equal(3, loaded.ShopBaseStock[0].Quantity);
            Assert.Equal("item.leather.helm", loaded.ShopBaseStock[1].BlueprintId);
            Assert.Equal(2, loaded.ShopBaseStock[1].Quantity);
        }

        [Fact]
        public async Task Absent_shop_block_reads_back_as_not_a_shopkeeper()
        {
            var original = new MobTemplate("mob.ordinary.rt")
            {
                Name = "Guard",
                IsShop = false,
            };

            var loaded = await RoundTrip(original);

            Assert.False(loaded.IsShop);
            Assert.Empty(loaded.ShopBaseStock);
        }

        [Fact]
        public void Deserialize_with_unknown_shop_currency_defaults_to_Coin_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.stale.currency.test
name: Stale Currency Mob
shop:
  acceptedCurrency: AstralGems
  tillSeed: 1000
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            // IsShop is true (shop block is present); currency falls back to Coin (the default).
            Assert.True(loaded.IsShop);
            Assert.Equal(CurrencyId.Coin, loaded.ShopAcceptedCurrency);
            Assert.Equal(1000, loaded.ShopTillSeed);
        }

        // ── Apply: ShopComponent + InventoryComponent added when IsShop ─────────

        [Fact]
        public async Task Apply_with_IsShop_adds_ShopComponent_and_InventoryComponent()
        {
            var original = new MobTemplate("mob.shopkeeper.apply.shop")
            {
                Name = "Shopkeeper",
                IsShop = true,
                ShopTillSeed = 10_000,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.test.dagger", Quantity = 1 },
                },
            };

            var loaded = await RoundTrip(original);
            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            Assert.True(ecs.HasComponent<ShopComponent>(entity.Id));
            Assert.True(ecs.HasComponent<InventoryComponent>(entity.Id));

            var shop = ecs.Get<ShopComponent>(entity.Id);
            Assert.Equal(10_000, shop.TillSeed);
            Assert.Single(shop.BaseStock);
            Assert.Equal("item.test.dagger", shop.BaseStock[0].BlueprintId);
        }

        [Fact]
        public async Task Apply_without_IsShop_does_not_add_ShopComponent()
        {
            var original = new MobTemplate("mob.noshop.apply")
            {
                Name = "Fighter",
                IsShop = false,
            };

            var loaded = await RoundTrip(original);
            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            Assert.False(ecs.HasComponent<ShopComponent>(entity.Id));
        }

        // ── SpawnHandler: till seeded, base stock spawned, no PersistentEntity ──

        [Fact]
        public async Task SpawnHandler_seeds_till_with_configured_TillSeed()
        {
            var mobTemplate = new MobTemplate("mob.till.seed.test")
            {
                Name = "Coin Dealer",
                IsShop = true,
                ShopAcceptedCurrency = CurrencyId.Coin,
                ShopTillSeed = 75_000,
            };

            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate);
            await RunSpawnHandler(ecs, registry);

            Assert.True(ecs.HasComponent<WalletComponent>(shopEntityId));
            var wallet = ecs.Get<WalletComponent>(shopEntityId);
            Assert.True(wallet.Balances.TryGetValue(CurrencyId.Coin, out var balance));
            Assert.Equal(75_000, balance);
        }

        [Fact]
        public async Task SpawnHandler_uses_DefaultTillSeed_when_TillSeed_is_zero()
        {
            var mobTemplate = new MobTemplate("mob.default.till.test")
            {
                Name = "Default Seed Merchant",
                IsShop = true,
                ShopTillSeed = 0, // defer to global default
            };

            var options = new ShopOptions { DefaultTillSeed = 200_000 };
            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate);
            await RunSpawnHandler(ecs, registry, options);

            var wallet = ecs.Get<WalletComponent>(shopEntityId);
            Assert.True(wallet.Balances.TryGetValue(CurrencyId.Coin, out var balance));
            Assert.Equal(200_000, balance);
        }

        [Fact]
        public async Task SpawnHandler_spawns_base_stock_with_ShopStockComponent_Base()
        {
            var itemTemplate = new MinimalItemTemplate("item.test.potion");

            var mobTemplate = new MobTemplate("mob.base.stock.test")
            {
                Name = "Potion Seller",
                IsShop = true,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.test.potion", Quantity = 3 },
                },
            };

            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate, itemTemplate);
            await RunSpawnHandler(ecs, registry);

            // Shopkeeper must have an InventoryComponent.
            Assert.True(ecs.HasComponent<InventoryComponent>(shopEntityId));
            var inv = ecs.Get<InventoryComponent>(shopEntityId);

            // Three items spawned.
            Assert.Equal(3, inv.ItemEntityIds.Count);

            // All carry ShopStockComponent { Base }.
            foreach (var itemId in inv.ItemEntityIds)
            {
                Assert.True(
                    ecs.HasComponent<ShopStockComponent>(itemId),
                    $"Item {itemId} is missing ShopStockComponent");
                var stockComp = ecs.Get<ShopStockComponent>(itemId);
                Assert.Equal(StockProvenance.Base, stockComp.Provenance);
                Assert.Null(stockComp.ExpiresAt);
            }
        }

        [Fact]
        public async Task SpawnHandler_does_not_add_PersistentEntity_to_shopkeeper()
        {
            var mobTemplate = new MobTemplate("mob.no.persist.test")
            {
                Name = "World Content Shopkeeper",
                IsShop = true,
                ShopTillSeed = 10_000,
            };

            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate);
            await RunSpawnHandler(ecs, registry);

            // INV-23: world content — no PersistentEntity.
            Assert.False(ecs.HasComponent<PersistentEntity>(shopEntityId));
        }

        [Fact]
        public async Task SpawnHandler_does_not_add_PersistentEntity_to_base_stock_items()
        {
            var itemTemplate = new MinimalItemTemplate("item.persist.check.item");

            var mobTemplate = new MobTemplate("mob.stock.no.persist.test")
            {
                Name = "Stock Test Shopkeeper",
                IsShop = true,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.persist.check.item", Quantity = 2 },
                },
            };

            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate, itemTemplate);
            await RunSpawnHandler(ecs, registry);

            var inv = ecs.Get<InventoryComponent>(shopEntityId);
            foreach (var itemId in inv.ItemEntityIds)
            {
                // INV-23: base-stock items are world content — no PersistentEntity.
                Assert.False(
                    ecs.HasComponent<PersistentEntity>(itemId),
                    $"Base-stock item {itemId} unexpectedly carries PersistentEntity");
            }
        }

        [Fact]
        public async Task SpawnHandler_ignores_unknown_base_stock_blueprints_without_throwing()
        {
            var mobTemplate = new MobTemplate("mob.unknown.stock.test")
            {
                Name = "Broken Merchant",
                IsShop = true,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = "item.does.not.exist", Quantity = 1 },
                },
            };

            // No item template registered → handler should log and skip, not throw.
            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate, stockItemTemplate: null);
            await RunSpawnHandler(ecs, registry);

            // Inventory exists but is empty (unknown blueprint skipped).
            Assert.True(ecs.HasComponent<InventoryComponent>(shopEntityId));
            var inv = ecs.Get<InventoryComponent>(shopEntityId);
            Assert.Empty(inv.ItemEntityIds);
        }

        [Fact]
        public async Task SpawnHandler_does_not_add_WalletComponent_when_till_seed_zero_and_default_is_zero()
        {
            var mobTemplate = new MobTemplate("mob.zero.till.test")
            {
                Name = "No-Till Shopkeeper",
                IsShop = true,
                ShopTillSeed = 0,
            };

            var options = new ShopOptions { DefaultTillSeed = 0 };
            var (shopEntityId, ecs, registry) = SpawnShopkeeper(mobTemplate);
            await RunSpawnHandler(ecs, registry, options);

            // No seed → no WalletComponent added by the handler (till is empty).
            // (The mob was not given one by Apply either.)
            Assert.False(ecs.HasComponent<WalletComponent>(shopEntityId));
        }

        // ── End-to-end: YAML → Apply → SpawnHandler ─────────────────────────────

        [Fact]
        public async Task EndToEnd_shopkeeper_authored_in_YAML_spawns_with_till_and_stock()
        {
            // 1. Author the template as YAML.
            var itemBp = "item.e2e.flask";
            var original = new MobTemplate("mob.e2e.shopkeeper")
            {
                Name = "E2E Merchant",
                IsShop = true,
                ShopAcceptedCurrency = CurrencyId.Coin,
                ShopTillSeed = 30_000,
                ShopBaseStock = new List<ShopStockRow>
                {
                    new() { BlueprintId = itemBp, Quantity = 2 },
                },
            };

            // 2. Round-trip through YAML.
            var loaded = await RoundTrip(original);

            // 3. Spawn via template registry (as WorldContentLoader does).
            var itemTemplate = new MinimalItemTemplate(itemBp);
            var (shopEntityId, ecs, registry) = SpawnShopkeeper(loaded, itemTemplate);

            // 4. Run the spawn handler (as Program.cs wires on WorldContentReadyEvent).
            await RunSpawnHandler(ecs, registry);

            // 5. Assert till seeded.
            Assert.True(ecs.HasComponent<WalletComponent>(shopEntityId));
            var wallet = ecs.Get<WalletComponent>(shopEntityId);
            Assert.Equal(30_000, wallet.Balances[CurrencyId.Coin]);

            // 6. Assert base stock in inventory with ShopStockComponent { Base }.
            var inv = ecs.Get<InventoryComponent>(shopEntityId);
            Assert.Equal(2, inv.ItemEntityIds.Count);
            foreach (var itemId in inv.ItemEntityIds)
            {
                Assert.True(ecs.HasComponent<ShopStockComponent>(itemId));
                Assert.Equal(StockProvenance.Base, ecs.Get<ShopStockComponent>(itemId).Provenance);
            }

            // 7. Assert no PersistentEntity on shopkeeper or stock.
            Assert.False(ecs.HasComponent<PersistentEntity>(shopEntityId));
            foreach (var itemId in inv.ItemEntityIds)
                Assert.False(ecs.HasComponent<PersistentEntity>(itemId));
        }
    }

    // ── Minimal item template for testing without a full item module ─────────────

    /// <summary>
    /// A bare-minimum <see cref="IEntityTemplate"/> that adds only <see cref="ItemDataComponent"/>
    /// so base-stock spawn tests don't require the full Items module.
    /// </summary>
    internal sealed class MinimalItemTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }

        public MinimalItemTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new ItemDataComponent
            {
                Name = "test item",
                Description = "A test item.",
            });
        }
    }
}
