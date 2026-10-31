using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.World.Events;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Flow tier — boot and reload through the real loader, <c>SpawnSystem</c> and
    /// <c>ShopkeeperSpawnHandler</c> dispatched in priority order (spawn-authoring T5): rules fill at
    /// State ahead of shop seeding, nothing is in limbo, and reload repopulates by rule id.
    /// </summary>
    public sealed class SpawnBootFlowTests
    {
        private static SpawnWorld Corpus(string rulesYaml)
        {
            var w = new SpawnWorld();
            w.Mob("mob.rat");
            w.Item("item.herb");
            w.Mob("mob.keeper", "shop:\n  tillSeed: 500\n  baseStock:\n    - blueprintId: item.herb\n      quantity: 2\n");
            w.Room("room.market", rulesYaml);
            return w;
        }

        private const string Rules =
            "  - blueprintId: mob.rat\n    count: 2\n" +
            "  - blueprintId: item.herb\n" +
            "  - blueprintId: mob.keeper\n";

        [Fact]
        public async Task Boot_places_every_rule_and_seeds_the_rule_placed_shopkeeper()
        {
            using var w = Corpus(Rules);

            await w.BootAsync();

            var room = w.RoomEntity("room.market");
            Assert.Equal(2, w.InRoom(room, "mob.rat").Count);
            Assert.Single(w.InRoom(room, "item.herb"));
            var keeper = Assert.Single(w.InRoom(room, "mob.keeper"));

            // Seeded in the same fan-out (State fill → Domain seeding).
            Assert.Equal(500, w.Ecs.Get<WalletComponent>(keeper).Balances[CurrencyId.Coin]);
            Assert.Equal(2, w.Ecs.Get<InventoryComponent>(keeper).ItemEntityIds.Count);

            // No limbo: every mob is placed; every item is placed or is shop stock.
            Assert.All(w.Ecs.GetAllComponents<MobDataComponent>(),
                m => Assert.True(w.Ecs.HasComponent<LocationComponent>(m.EntityId)));
            Assert.All(w.Ecs.GetAllComponents<ItemDataComponent>(),
                i => Assert.True(w.Ecs.HasComponent<LocationComponent>(i.EntityId) || w.Ecs.HasComponent<ShopStockComponent>(i.EntityId)));
        }

        [Fact]
        public async Task Reload_repopulates_with_fresh_ids_and_reordering_rules_changes_no_per_id_population()
        {
            using var w = Corpus(Rules);
            await w.BootAsync();
            var before = w.Ecs.GetAllComponents<MobDataComponent>().Select(e => e.EntityId).ToHashSet();

            // Reorder the YAML rules, then reload.
            w.Room("room.market",
                "  - blueprintId: mob.keeper\n" +
                "  - blueprintId: item.herb\n" +
                "  - blueprintId: mob.rat\n    count: 2\n");
            await w.Loader.ReloadAsync();
            await w.Bus.PublishAsync(new WorldContentReadyEvent());

            var room = w.RoomEntity("room.market");
            var mobs = w.Ecs.GetAllComponents<MobDataComponent>().Select(e => e.EntityId).ToList();
            Assert.Equal(3, mobs.Count);
            Assert.Empty(mobs.Intersect(before));

            var live = w.Spawn.DescribeRoom(room).ToDictionary(s => s.RuleId, s => s.Live);
            Assert.Equal(new Dictionary<string, int> { ["mob.keeper"] = 1, ["item.herb"] = 1, ["mob.rat"] = 2 }, live);
        }
    }
}
