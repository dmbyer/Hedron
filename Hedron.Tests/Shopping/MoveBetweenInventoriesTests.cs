using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Xunit;

namespace Hedron.Tests.Shopping
{
    /// <summary>
    /// Tier 1 unit tests for <see cref="IItemSystem.MoveBetweenInventories"/> (WP-2, Items module).
    ///
    /// Coverage contract (shopping.md Test plan):
    ///   • Item id leaves the source holder's <c>InventoryComponent</c> and appears in the
    ///     destination's.
    ///   • No <c>LocationComponent</c> is added or removed.
    ///   • <c>BlueprintComponent</c> is NOT mutated (INV-21).
    ///   • Silent no-op when the item is not in the source holder's inventory (race-condition
    ///     handling — mirrors <c>MoveToInventory</c> precedent).
    /// </summary>
    public sealed class MoveBetweenInventoriesTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (ItemSystem system, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            return (new ItemSystem(ecs), ecs);
        }

        private static uint MakeHolder(EntityService ecs)
        {
            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new InventoryComponent());
            return entity.Id;
        }

        private static uint MakeItemInInventory(EntityService ecs, uint holderId,
            string? blueprintId = null, long value = 0)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "item", Value = value });
            if (blueprintId != null)
                ecs.AddComponent(item.Id, new BlueprintComponent { BlueprintId = blueprintId });
            ecs.Get<InventoryComponent>(holderId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        // ── Core move semantics ──────────────────────────────────────────────────

        [Fact]
        public void MoveBetweenInventories_item_removed_from_source_inventory()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from);

            sys.MoveBetweenInventories(itemId, from, to);

            Assert.DoesNotContain(itemId, ecs.Get<InventoryComponent>(from).ItemEntityIds);
        }

        [Fact]
        public void MoveBetweenInventories_item_added_to_destination_inventory()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from);

            sys.MoveBetweenInventories(itemId, from, to);

            Assert.Contains(itemId, ecs.Get<InventoryComponent>(to).ItemEntityIds);
        }

        // ── No LocationComponent added ────────────────────────────────────────────

        [Fact]
        public void MoveBetweenInventories_does_not_add_LocationComponent()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from);

            sys.MoveBetweenInventories(itemId, from, to);

            Assert.False(ecs.HasComponent<LocationComponent>(itemId),
                "MoveBetweenInventories must not attach a LocationComponent (inventory→inventory only).");
        }

        [Fact]
        public void MoveBetweenInventories_does_not_remove_existing_LocationComponent()
        {
            // Edge: if for some reason an item already had a LocationComponent, it must be left untouched.
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from);
            // Attach a LocationComponent manually (unusual but should be preserved unchanged).
            ecs.AddComponent(itemId, new LocationComponent { RoomEntityId = 42u });

            sys.MoveBetweenInventories(itemId, from, to);

            // LocationComponent should still be present — not our concern.
            Assert.True(ecs.HasComponent<LocationComponent>(itemId));
            Assert.Equal(42u, ecs.Get<LocationComponent>(itemId).RoomEntityId);
        }

        // ── INV-21: BlueprintComponent preservation ───────────────────────────────

        [Fact]
        public void MoveBetweenInventories_does_NOT_clear_BlueprintComponent_INV21()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from, blueprintId: "item.sword.01");

            sys.MoveBetweenInventories(itemId, from, to);

            Assert.True(ecs.HasComponent<BlueprintComponent>(itemId),
                "INV-21: BlueprintComponent must be preserved as an origin record.");
            Assert.Equal("item.sword.01", ecs.Get<BlueprintComponent>(itemId).BlueprintId);
        }

        // ── Silent no-op on race condition ───────────────────────────────────────

        [Fact]
        public void MoveBetweenInventories_noop_when_item_not_in_source_inventory()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);

            // Item exists but is NOT in `from`'s inventory.
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "orphan" });

            // Should not throw.
            sys.MoveBetweenInventories(item.Id, from, to);

            // Item appears in destination (best-effort append).
            Assert.Contains(item.Id, ecs.Get<InventoryComponent>(to).ItemEntityIds);
        }

        [Fact]
        public void MoveBetweenInventories_does_not_mutate_other_items_in_source_inventory()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemToMove = MakeItemInInventory(ecs, from);
            var itemToStay = MakeItemInInventory(ecs, from);

            sys.MoveBetweenInventories(itemToMove, from, to);

            Assert.Contains(itemToStay, ecs.Get<InventoryComponent>(from).ItemEntityIds);
        }

        // ── GetItemsInInventory consistency ──────────────────────────────────────

        [Fact]
        public void MoveBetweenInventories_item_visible_in_destination_via_GetItemsInInventory()
        {
            var (sys, ecs) = Build();
            var from = MakeHolder(ecs);
            var to = MakeHolder(ecs);
            var itemId = MakeItemInInventory(ecs, from);

            sys.MoveBetweenInventories(itemId, from, to);

            Assert.Contains(itemId, sys.GetItemsInInventory(to));
            Assert.DoesNotContain(itemId, sys.GetItemsInInventory(from));
        }
    }
}
