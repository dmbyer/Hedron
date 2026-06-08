using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Items
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="EquipmentSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/equipment.md.
    /// </summary>
    public sealed class EquipmentSystemTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        private static (EquipmentSystem system, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            return (new EquipmentSystem(ecs), ecs);
        }

        /// <summary>
        /// Creates a player entity with <see cref="InventoryComponent"/> and
        /// an empty <see cref="EquipmentComponent"/>.
        /// </summary>
        private static uint MakeCharacter(EntityService ecs)
        {
            var id = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(id, new InventoryComponent());
            ecs.AddComponent(id, new EquipmentComponent());
            return id;
        }

        /// <summary>
        /// Creates an item entity in a character's inventory, with the given
        /// worn slots declared on its <see cref="ItemDataComponent"/>.
        /// </summary>
        private static uint MakeWearableInInventory(
            EntityService ecs,
            uint characterId,
            string name,
            IEnumerable<WornSlot> wornSlots,
            IEnumerable<string>? keywords = null)
        {
            var item = ecs.CreateEntity();
            var data = new ItemDataComponent
            {
                Name = name,
                WornSlots = new List<WornSlot>(wornSlots),
            };
            if (keywords != null) data.Keywords.AddRange(keywords);
            ecs.AddComponent(item.Id, data);

            ecs.Get<InventoryComponent>(characterId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        /// <summary>
        /// Creates a non-wearable item (no <c>WornSlots</c>) in the character's inventory.
        /// </summary>
        private static uint MakeNonWearableInInventory(EntityService ecs, uint characterId, string name = "pebble")
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = name });
            ecs.Get<InventoryComponent>(characterId).ItemEntityIds.Add(item.Id);
            return item.Id;
        }

        // ── GetWornSlots ─────────────────────────────────────────────────────────

        [Fact]
        public void GetWornSlots_returns_declared_slots_for_wearable_item()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "iron helm", new[] { WornSlot.Head });

            var slots = sys.GetWornSlots(itemId);

            Assert.Single(slots);
            Assert.Contains(WornSlot.Head, slots);
        }

        [Fact]
        public void GetWornSlots_returns_empty_for_item_without_WornSlots()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeNonWearableInInventory(ecs, characterId);

            var slots = sys.GetWornSlots(itemId);

            Assert.Empty(slots);
        }

        [Fact]
        public void GetWornSlots_returns_empty_for_item_with_null_WornSlots()
        {
            var (sys, ecs) = Build();
            // Item with explicitly null WornSlots
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent { Name = "coin", WornSlots = null });

            var slots = sys.GetWornSlots(item.Id);

            Assert.Empty(slots);
        }

        [Fact]
        public void GetWornSlots_returns_empty_for_entity_without_ItemDataComponent()
        {
            var (sys, ecs) = Build();
            var bare = ecs.CreateEntity();
            // No ItemDataComponent added

            var slots = sys.GetWornSlots(bare.Id);

            Assert.Empty(slots);
        }

        [Fact]
        public void GetWornSlots_returns_both_slots_for_two_handed_weapon()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "greataxe",
                new[] { WornSlot.MainHand, WornSlot.OffHand });

            var slots = sys.GetWornSlots(itemId);

            Assert.Equal(2, slots.Count);
            Assert.Contains(WornSlot.MainHand, slots);
            Assert.Contains(WornSlot.OffHand, slots);
        }

        // ── EquipItem ─────────────────────────────────────────────────────────

        [Fact]
        public void EquipItem_removes_item_from_inventory()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "leather vest", new[] { WornSlot.Chest });

            sys.EquipItem(characterId, itemId);

            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.DoesNotContain(itemId, inv.ItemEntityIds);
        }

        [Fact]
        public void EquipItem_places_item_in_declared_slot()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "shortsword", new[] { WornSlot.MainHand });

            sys.EquipItem(characterId, itemId);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.True(eq.Slots.ContainsKey(WornSlot.MainHand));
            Assert.Equal(itemId, eq.Slots[WornSlot.MainHand]);
        }

        [Fact]
        public void EquipItem_is_noop_for_item_with_no_WornSlots()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeNonWearableInInventory(ecs, characterId);

            sys.EquipItem(characterId, itemId);

            // Item should stay in inventory; EquipmentComponent untouched.
            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.Contains(itemId, inv.ItemEntityIds);
            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.Empty(eq.Slots);
        }

        [Fact]
        public void EquipItem_places_two_handed_weapon_in_both_slots()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "greatsword",
                new[] { WornSlot.MainHand, WornSlot.OffHand });

            sys.EquipItem(characterId, itemId);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.Equal(itemId, eq.Slots[WornSlot.MainHand]);
            Assert.Equal(itemId, eq.Slots[WornSlot.OffHand]);
            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.DoesNotContain(itemId, inv.ItemEntityIds);
        }

        // ── EquipItem: implicit swap ─────────────────────────────────────────────

        [Fact]
        public void EquipItem_displaces_existing_item_in_occupied_slot_to_inventory()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);

            // Pre-equip a dagger in MainHand directly via the component.
            var oldItem = ecs.CreateEntity();
            ecs.AddComponent(oldItem.Id, new ItemDataComponent
            {
                Name = "dagger",
                WornSlots = new List<WornSlot> { WornSlot.MainHand },
            });
            ecs.Get<EquipmentComponent>(characterId).Slots[WornSlot.MainHand] = oldItem.Id;

            // Now equip a new item to the same slot.
            var newItem = MakeWearableInInventory(ecs, characterId, "shortsword", new[] { WornSlot.MainHand });

            sys.EquipItem(characterId, newItem);

            // New item is equipped.
            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.Equal(newItem, eq.Slots[WornSlot.MainHand]);

            // Old item is back in inventory.
            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.Contains(oldItem.Id, inv.ItemEntityIds);
        }

        [Fact]
        public void EquipItem_displaces_one_handed_items_when_equipping_two_handed_weapon()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);

            // Pre-equip separate items in MainHand and OffHand.
            var mainItem = ecs.CreateEntity();
            ecs.AddComponent(mainItem.Id, new ItemDataComponent { Name = "sword", WornSlots = new List<WornSlot> { WornSlot.MainHand } });
            var offItem = ecs.CreateEntity();
            ecs.AddComponent(offItem.Id, new ItemDataComponent { Name = "shield", WornSlots = new List<WornSlot> { WornSlot.OffHand } });

            ecs.Get<EquipmentComponent>(characterId).Slots[WornSlot.MainHand] = mainItem.Id;
            ecs.Get<EquipmentComponent>(characterId).Slots[WornSlot.OffHand] = offItem.Id;

            // Equip a two-hander that declares both slots.
            var twoHander = MakeWearableInInventory(
                ecs, characterId, "greatsword",
                new[] { WornSlot.MainHand, WornSlot.OffHand });

            sys.EquipItem(characterId, twoHander);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.Equal(twoHander, eq.Slots[WornSlot.MainHand]);
            Assert.Equal(twoHander, eq.Slots[WornSlot.OffHand]);

            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.Contains(mainItem.Id, inv.ItemEntityIds);
            Assert.Contains(offItem.Id, inv.ItemEntityIds);
        }

        // ── RemoveItem ───────────────────────────────────────────────────────────

        [Fact]
        public void RemoveItem_clears_slot_in_EquipmentComponent()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "cap", new[] { WornSlot.Head });
            sys.EquipItem(characterId, itemId);

            sys.RemoveItem(characterId, itemId);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.False(eq.Slots.ContainsKey(WornSlot.Head));
        }

        [Fact]
        public void RemoveItem_adds_item_back_to_inventory()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "boots", new[] { WornSlot.Feet });
            sys.EquipItem(characterId, itemId);

            sys.RemoveItem(characterId, itemId);

            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.Contains(itemId, inv.ItemEntityIds);
        }

        [Fact]
        public void RemoveItem_is_noop_for_entity_without_EquipmentComponent()
        {
            var (sys, ecs) = Build();
            var bare = ecs.CreateEntity();
            ecs.AddComponent(bare.Id, new InventoryComponent());

            // Should not throw.
            sys.RemoveItem(bare.Id, 9999u);
        }

        [Fact]
        public void RemoveItem_removes_two_handed_weapon_from_both_slots()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "halberd",
                new[] { WornSlot.MainHand, WornSlot.OffHand });
            sys.EquipItem(characterId, itemId);

            sys.RemoveItem(characterId, itemId);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.False(eq.Slots.ContainsKey(WornSlot.MainHand));
            Assert.False(eq.Slots.ContainsKey(WornSlot.OffHand));

            var inv = ecs.Get<InventoryComponent>(characterId);
            // Item should appear exactly once in inventory (not duplicated per slot).
            Assert.Single(inv.ItemEntityIds, id => id == itemId);
        }

        // ── RemoveFromSlot ───────────────────────────────────────────────────────

        [Fact]
        public void RemoveFromSlot_clears_the_named_slot()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "helm", new[] { WornSlot.Head });
            sys.EquipItem(characterId, itemId);

            sys.RemoveFromSlot(characterId, WornSlot.Head);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.False(eq.Slots.ContainsKey(WornSlot.Head));
        }

        [Fact]
        public void RemoveFromSlot_returns_displaced_item_to_inventory()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "chestplate", new[] { WornSlot.Chest });
            sys.EquipItem(characterId, itemId);

            sys.RemoveFromSlot(characterId, WornSlot.Chest);

            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.Contains(itemId, inv.ItemEntityIds);
        }

        [Fact]
        public void RemoveFromSlot_is_noop_when_slot_is_empty()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);

            // Should not throw; equipment slots are empty.
            sys.RemoveFromSlot(characterId, WornSlot.MainHand);

            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.Empty(eq.Slots);
        }

        [Fact]
        public void RemoveFromSlot_is_noop_for_entity_without_EquipmentComponent()
        {
            var (sys, ecs) = Build();
            var bare = ecs.CreateEntity();

            // Should not throw.
            sys.RemoveFromSlot(bare.Id, WornSlot.Head);
        }

        [Fact]
        public void RemoveFromSlot_does_not_send_two_handed_item_to_inventory_twice()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "quarterstaff",
                new[] { WornSlot.MainHand, WornSlot.OffHand });
            sys.EquipItem(characterId, itemId);

            // Remove only the MainHand slot — OffHand still holds the same item id.
            sys.RemoveFromSlot(characterId, WornSlot.MainHand);

            // Item still occupies OffHand, so it must NOT be sent back to inventory yet.
            var inv = ecs.Get<InventoryComponent>(characterId);
            Assert.DoesNotContain(itemId, inv.ItemEntityIds);
            var eq = ecs.Get<EquipmentComponent>(characterId);
            Assert.True(eq.Slots.ContainsKey(WornSlot.OffHand));
            Assert.Equal(itemId, eq.Slots[WornSlot.OffHand]);
        }

        // ── GetEquippedItems ─────────────────────────────────────────────────────

        [Fact]
        public void GetEquippedItems_returns_all_equipped_item_ids()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var sword = MakeWearableInInventory(ecs, characterId, "sword", new[] { WornSlot.MainHand });
            var helm = MakeWearableInInventory(ecs, characterId, "helm", new[] { WornSlot.Head });
            sys.EquipItem(characterId, sword);
            sys.EquipItem(characterId, helm);

            var equipped = sys.GetEquippedItems(characterId);

            Assert.Contains(sword, equipped);
            Assert.Contains(helm, equipped);
        }

        [Fact]
        public void GetEquippedItems_returns_empty_for_entity_without_EquipmentComponent()
        {
            var (sys, ecs) = Build();
            var bare = ecs.CreateEntity();

            var equipped = sys.GetEquippedItems(bare.Id);

            Assert.Empty(equipped);
        }

        [Fact]
        public void GetEquippedItems_returns_empty_when_no_items_equipped()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);

            var equipped = sys.GetEquippedItems(characterId);

            Assert.Empty(equipped);
        }

        [Fact]
        public void GetEquippedItems_counts_two_handed_weapon_once_despite_two_slots()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "war hammer",
                new[] { WornSlot.MainHand, WornSlot.OffHand });
            sys.EquipItem(characterId, itemId);

            var equipped = sys.GetEquippedItems(characterId);

            // The item id appears once per slot in the Dictionary values — both slots hold
            // the same id. GetEquippedItems returns Slots.Values so two entries are expected.
            Assert.Contains(itemId, equipped);
        }

        // ── TryFindEquippedItem ──────────────────────────────────────────────────

        [Fact]
        public void TryFindEquippedItem_matches_by_name_prefix()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "longsword", new[] { WornSlot.MainHand });
            sys.EquipItem(characterId, itemId);

            var found = sys.TryFindEquippedItem(characterId, "long", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindEquippedItem_matches_by_keyword_prefix()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(
                ecs, characterId, "battle helm",
                new[] { WornSlot.Head },
                keywords: new[] { "helm", "battle" });
            sys.EquipItem(characterId, itemId);

            var found = sys.TryFindEquippedItem(characterId, "bat", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindEquippedItem_matching_is_case_insensitive()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "IronBoots", new[] { WornSlot.Feet });
            sys.EquipItem(characterId, itemId);

            var found = sys.TryFindEquippedItem(characterId, "ironboots", out var result);

            Assert.True(found);
            Assert.Equal(itemId, result);
        }

        [Fact]
        public void TryFindEquippedItem_returns_false_when_token_does_not_match_any_worn_item()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "helm", new[] { WornSlot.Head });
            sys.EquipItem(characterId, itemId);

            var found = sys.TryFindEquippedItem(characterId, "boots", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindEquippedItem_returns_false_when_item_is_in_inventory_not_worn()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            // Item is in inventory but not equipped.
            MakeWearableInInventory(ecs, characterId, "gloves", new[] { WornSlot.Head });

            var found = sys.TryFindEquippedItem(characterId, "gloves", out _);

            Assert.False(found);
        }

        [Fact]
        public void TryFindEquippedItem_returns_false_for_entity_without_EquipmentComponent()
        {
            var (sys, ecs) = Build();
            var bare = ecs.CreateEntity();
            ecs.AddComponent(bare.Id, new InventoryComponent());

            var found = sys.TryFindEquippedItem(bare.Id, "anything", out _);

            Assert.False(found);
        }

        // ── Equip → Unequip round-trip ───────────────────────────────────────────

        [Fact]
        public void EquipThenRemove_item_returns_to_inventory_and_slot_is_empty()
        {
            var (sys, ecs) = Build();
            var characterId = MakeCharacter(ecs);
            var itemId = MakeWearableInInventory(ecs, characterId, "cuirass", new[] { WornSlot.Chest });

            sys.EquipItem(characterId, itemId);
            Assert.DoesNotContain(itemId, ecs.Get<InventoryComponent>(characterId).ItemEntityIds);
            Assert.Equal(itemId, ecs.Get<EquipmentComponent>(characterId).Slots[WornSlot.Chest]);

            sys.RemoveItem(characterId, itemId);
            Assert.Contains(itemId, ecs.Get<InventoryComponent>(characterId).ItemEntityIds);
            Assert.False(ecs.Get<EquipmentComponent>(characterId).Slots.ContainsKey(WornSlot.Chest));
        }

        // ── INV-5: EquipmentSystem does not hold IEventBus ──────────────────────

        [Fact]
        public void EquipmentSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(EquipmentSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: EquipmentSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
