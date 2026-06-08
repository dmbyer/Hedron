using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Items
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="ItemSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/items-and-inventory.md.
    /// </summary>
    public sealed class ItemSystemTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fresh <see cref="ItemSystem"/> backed by a new <see cref="EntityService"/>.
        /// </summary>
        private static (ItemSystem system, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            return (new ItemSystem(ecs), ecs);
        }

        /// <summary>
        /// Creates an item entity on the ground in <paramref name="roomEntityId"/>.
        /// Adds <see cref="ItemDataComponent"/> and <see cref="LocationComponent"/> directly
        /// so the item shows up in room queries.
        /// </summary>
        private static uint MakeItemInRoom(
            EntityService ecs,
            uint roomEntityId,
            string name = "sword",
            IEnumerable<string>? keywords = null,
            string? blueprintId = null)
        {
            var item = ecs.CreateEntity();
            var data = new ItemDataComponent { Name = name };
            if (keywords != null) data.Keywords.AddRange(keywords);
            ecs.AddComponent(item.Id, data);
            ecs.AddComponent(item.Id, new LocationComponent { RoomEntityId = roomEntityId, RoomBlueprintId = roomEntityId.ToString() });
            if (blueprintId != null)
                ecs.AddComponent(item.Id, new BlueprintComponent { BlueprintId = blueprintId });
            return item.Id;
        }

        /// <summary>
        /// Creates a player entity with an <see cref="InventoryComponent"/> attached.
        /// </summary>
        private static uint MakePlayer(EntityService ecs)
        {
            var id = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(id, new InventoryComponent());
            return id;
        }

        // ── GetItemsInRoom ───────────────────────────────────────────────────────

        [Fact]
        public void GetItemsInRoom_returns_items_with_matching_RoomEntityId()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var itemId = MakeItemInRoom(ecs, roomId);

            var result = sys.GetItemsInRoom(roomId);

            Assert.Contains(itemId, result);
        }

        [Fact]
        public void GetItemsInRoom_excludes_items_in_a_different_room()
        {
            var (sys, ecs) = Build();
            var roomA = 100u;
            var roomB = 200u;
            var itemInA = MakeItemInRoom(ecs, roomA);

            var result = sys.GetItemsInRoom(roomB);

            Assert.DoesNotContain(itemInA, result);
        }

        [Fact]
        public void GetItemsInRoom_excludes_entities_without_ItemDataComponent()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;

            // Entity with only LocationComponent, no ItemDataComponent — not an item.
            var nonItem = ecs.CreateEntity();
            ecs.AddComponent(nonItem.Id, new LocationComponent { RoomEntityId = roomId });

            var result = sys.GetItemsInRoom(roomId);

            Assert.DoesNotContain(nonItem.Id, result);
        }

        [Fact]
        public void GetItemsInRoom_excludes_items_in_inventory_no_LocationComponent()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;

            // Item in inventory has no LocationComponent.
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "dagger" });
            // Deliberately no LocationComponent

            var result = sys.GetItemsInRoom(roomId);

            Assert.DoesNotContain(item.Id, result);
        }

        [Fact]
        public void GetItemsInRoom_returns_empty_for_empty_room()
        {
            var (sys, _) = Build();
            var result = sys.GetItemsInRoom(999u);
            Assert.Empty(result);
        }

        [Fact]
        public void GetItemsInRoom_returns_multiple_items_when_several_are_present()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var item1 = MakeItemInRoom(ecs, roomId, "sword");
            var item2 = MakeItemInRoom(ecs, roomId, "shield");

            var result = sys.GetItemsInRoom(roomId);

            Assert.Contains(item1, result);
            Assert.Contains(item2, result);
            Assert.Equal(2, result.Count);
        }

        // ── GetItemsInInventory ──────────────────────────────────────────────────

        [Fact]
        public void GetItemsInInventory_returns_items_in_InventoryComponent()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "pouch" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            var result = sys.GetItemsInInventory(player);

            Assert.Contains(item.Id, result);
        }

        [Fact]
        public void GetItemsInInventory_returns_empty_for_entity_without_InventoryComponent()
        {
            var (sys, ecs) = Build();
            var noInv = new EntityBuilder(ecs).AsPlayer().Build(); // no InventoryComponent

            var result = sys.GetItemsInInventory(noInv);

            Assert.Empty(result);
        }

        [Fact]
        public void GetItemsInInventory_returns_empty_for_empty_inventory()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);

            var result = sys.GetItemsInInventory(player);

            Assert.Empty(result);
        }

        // ── TryFindItemInRoom ────────────────────────────────────────────────────

        [Fact]
        public void TryFindItemInRoom_matches_item_by_name_prefix()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var itemId = MakeItemInRoom(ecs, roomId, name: "longsword");

            var found = sys.TryFindItemInRoom(roomId, "long", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindItemInRoom_matches_item_by_exact_name()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var itemId = MakeItemInRoom(ecs, roomId, name: "sword");

            var found = sys.TryFindItemInRoom(roomId, "sword", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindItemInRoom_matches_item_by_keyword_prefix()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var itemId = MakeItemInRoom(ecs, roomId, name: "a glowing orb", keywords: new[] { "orb", "glowing" });

            var found = sys.TryFindItemInRoom(roomId, "glow", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindItemInRoom_matching_is_case_insensitive()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var itemId = MakeItemInRoom(ecs, roomId, name: "IronShield");

            var found = sys.TryFindItemInRoom(roomId, "ironshield", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindItemInRoom_returns_false_when_token_not_in_room()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            MakeItemInRoom(ecs, roomId, name: "sword");

            var found = sys.TryFindItemInRoom(roomId, "potion", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindItemInRoom_returns_false_for_item_in_different_room()
        {
            var (sys, ecs) = Build();
            var roomA = 100u;
            var roomB = 200u;
            MakeItemInRoom(ecs, roomA, name: "sword");

            var found = sys.TryFindItemInRoom(roomB, "sword", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindItemInRoom_returns_false_when_item_is_in_inventory_not_room()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);

            // Item in inventory (no LocationComponent).
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "dagger" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            var found = sys.TryFindItemInRoom(roomId, "dagger", out _);

            Assert.False(found);
        }

        // ── TryFindItemInInventory ───────────────────────────────────────────────

        [Fact]
        public void TryFindItemInInventory_matches_item_by_name_prefix()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "healing potion", Keywords = { "potion", "healing" } });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            var found = sys.TryFindItemInInventory(player, "heal", out var result);

            Assert.True(found);
            Assert.Equal(item.Id, result);
        }

        [Fact]
        public void TryFindItemInInventory_matches_item_by_keyword()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "healing potion", Keywords = { "potion" } });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            var found = sys.TryFindItemInInventory(player, "pot", out var result);

            Assert.True(found);
            Assert.Equal(item.Id, result);
        }

        [Fact]
        public void TryFindItemInInventory_returns_false_when_item_not_carried()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);

            var found = sys.TryFindItemInInventory(player, "sword", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindItemInInventory_returns_false_for_item_in_room_not_inventory()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            MakeItemInRoom(ecs, roomId, name: "sword");

            // Player carries nothing — the sword is on the ground.
            var found = sys.TryFindItemInInventory(player, "sword", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindItemInInventory_returns_false_for_holder_without_InventoryComponent()
        {
            var (sys, ecs) = Build();
            var noInv = new EntityBuilder(ecs).AsPlayer().Build();

            var found = sys.TryFindItemInInventory(noInv, "anything", out _);

            Assert.False(found);
        }

        // ── MoveToInventory (pickup) ─────────────────────────────────────────────

        [Fact]
        public void MoveToInventory_removes_LocationComponent_from_item()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId);

            sys.MoveToInventory(itemId, player);

            Assert.False(ecs.HasComponent<LocationComponent>(itemId));
        }

        [Fact]
        public void MoveToInventory_appends_item_to_InventoryComponent()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId);

            sys.MoveToInventory(itemId, player);

            var inv = ecs.Get<InventoryComponent>(player);
            Assert.Contains(itemId, inv.ItemEntityIds);
        }

        /// <summary>
        /// INV-21: BlueprintComponent must NOT be cleared on item pickup.
        /// It is preserved as an origin record; spawn-slot vacancy is tracked
        /// by SpawnSystem via domain events, not by clearing BlueprintComponent.
        /// </summary>
        [Fact]
        public void MoveToInventory_does_NOT_clear_BlueprintComponent_INV21()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId, blueprintId: "item.bp.001");

            sys.MoveToInventory(itemId, player);

            Assert.True(
                ecs.HasComponent<BlueprintComponent>(itemId),
                "INV-21: BlueprintComponent must be preserved on pickup as an origin record; " +
                "it must not be cleared by MoveToInventory.");
            Assert.Equal("item.bp.001", ecs.Get<BlueprintComponent>(itemId).BlueprintId);
        }

        [Fact]
        public void MoveToInventory_is_noop_when_item_has_no_LocationComponent()
        {
            var (sys, ecs) = Build();
            var player = MakePlayer(ecs);

            // Item in inventory already (no LocationComponent).
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "ring" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            // A second call must not throw or duplicate the item.
            sys.MoveToInventory(item.Id, player);

            var inv = ecs.Get<InventoryComponent>(player);
            Assert.Single(inv.ItemEntityIds); // still exactly one entry
        }

        [Fact]
        public void MoveToInventory_item_no_longer_appears_in_GetItemsInRoom()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId);

            sys.MoveToInventory(itemId, player);

            Assert.DoesNotContain(itemId, sys.GetItemsInRoom(roomId));
        }

        [Fact]
        public void MoveToInventory_item_appears_in_GetItemsInInventory()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId);

            sys.MoveToInventory(itemId, player);

            Assert.Contains(itemId, sys.GetItemsInInventory(player));
        }

        // ── DropToRoom (drop) ────────────────────────────────────────────────────

        [Fact]
        public void DropToRoom_removes_item_from_InventoryComponent()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);

            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "torch" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, roomId);

            var inv = ecs.Get<InventoryComponent>(player);
            Assert.DoesNotContain(item.Id, inv.ItemEntityIds);
        }

        [Fact]
        public void DropToRoom_attaches_LocationComponent_to_item()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);

            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "scroll" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, roomId);

            Assert.True(ecs.HasComponent<LocationComponent>(item.Id));
            var loc = ecs.Get<LocationComponent>(item.Id);
            Assert.Equal(roomId, loc.RoomEntityId);
        }

        [Fact]
        public void DropToRoom_item_appears_in_GetItemsInRoom()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);

            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "map" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, roomId);

            Assert.Contains(item.Id, sys.GetItemsInRoom(roomId));
        }

        [Fact]
        public void DropToRoom_item_no_longer_in_GetItemsInInventory()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);

            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "key" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, roomId);

            Assert.DoesNotContain(item.Id, sys.GetItemsInInventory(player));
        }

        [Fact]
        public void DropToRoom_copies_RoomBlueprintId_from_room_BlueprintComponent_when_present()
        {
            var (sys, ecs) = Build();

            // Create a room entity with a BlueprintComponent.
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = "room.start" });

            var player = MakePlayer(ecs);
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "gem" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, room.Id);

            var loc = ecs.Get<LocationComponent>(item.Id);
            Assert.Equal(room.Id, loc.RoomEntityId);
            Assert.Equal("room.start", loc.RoomBlueprintId);
        }

        [Fact]
        public void DropToRoom_sets_null_RoomBlueprintId_when_room_has_no_BlueprintComponent()
        {
            var (sys, ecs) = Build();
            var roomId = 500u; // No entity created for room; no BlueprintComponent
            var player = MakePlayer(ecs);

            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "coin" });
            ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item.Id);

            sys.DropToRoom(item.Id, player, roomId);

            var loc = ecs.Get<LocationComponent>(item.Id);
            Assert.Equal(roomId, loc.RoomEntityId);
            Assert.Null(loc.RoomBlueprintId);
        }

        // ── Pickup → Drop round-trip ─────────────────────────────────────────────

        [Fact]
        public void PickupThenDrop_item_returns_to_room_queries()
        {
            var (sys, ecs) = Build();
            var roomId = 100u;
            var player = MakePlayer(ecs);
            var itemId = MakeItemInRoom(ecs, roomId, name: "lantern");

            sys.MoveToInventory(itemId, player);
            Assert.DoesNotContain(itemId, sys.GetItemsInRoom(roomId)); // carried

            sys.DropToRoom(itemId, player, roomId);
            Assert.Contains(itemId, sys.GetItemsInRoom(roomId)); // back on ground
            Assert.DoesNotContain(itemId, sys.GetItemsInInventory(player));
        }

        // ── INV-5: ItemSystem does not hold IEventBus ────────────────────────────

        [Fact]
        public void ItemSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(ItemSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: ItemSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
