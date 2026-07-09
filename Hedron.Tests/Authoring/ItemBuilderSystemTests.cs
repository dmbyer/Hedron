using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="ItemBuilderSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/items-and-inventory.md
    /// (authoring / builder section) and the interface <see cref="IItemBuilderSystem"/>.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>
    /// (no mocking framework).
    /// </summary>
    public sealed class ItemBuilderSystemTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private static (ItemBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new ItemBuilderSystem(ecs, registry, NullLogger<ItemBuilderSystem>.Instance);
            return (system, ecs, registry);
        }

        /// <summary>Creates a room entity with a BlueprintComponent so CreateItem can derive SpawnRoomBlueprintId.</summary>
        private static uint MakeRoom(EntityService ecs, string blueprintId = "room.test")
        {
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = blueprintId });
            return room.Id;
        }

        // ── CreateItem ───────────────────────────────────────────────────────────

        [Fact]
        public void CreateItem_returns_nonzero_ItemEntityId()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Iron Sword", roomId);
            Assert.NotEqual(0u, result.ItemEntityId);
        }

        [Fact]
        public void CreateItem_returns_nonempty_BlueprintId()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Iron Sword", roomId);
            Assert.False(string.IsNullOrWhiteSpace(result.BlueprintId));
        }

        [Fact]
        public void CreateItem_attaches_ItemDataComponent_with_correct_name()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Steel Dagger", roomId);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal("Steel Dagger", item.Name);
        }

        [Fact]
        public void CreateItem_attaches_BlueprintComponent_with_matching_id()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Shield", roomId);

            var bp = ecs.Get<BlueprintComponent>(result.ItemEntityId);
            Assert.Equal(result.BlueprintId, bp.BlueprintId);
        }

        [Fact]
        public void CreateItem_attaches_LocationComponent_pointing_to_room()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Torch", roomId);

            var loc = ecs.Get<LocationComponent>(result.ItemEntityId);
            Assert.Equal(roomId, loc.RoomEntityId);
        }

        [Fact]
        public void CreateItem_LocationComponent_copies_SpawnRoomBlueprintId_from_room_BlueprintComponent()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs, "room.tavern");
            var result = sys.CreateItem("Mug", roomId);

            var loc = ecs.Get<LocationComponent>(result.ItemEntityId);
            Assert.Equal("room.tavern", loc.RoomBlueprintId);
        }

        [Fact]
        public void CreateItem_registers_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Map", roomId);

            var found = registry.TryGet(result.BlueprintId, out var template);
            Assert.True(found);
            Assert.NotNull(template);
        }

        [Fact]
        public void CreateItem_template_has_correct_name()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Golden Key", roomId);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("Golden Key", itemTemplate.Name);
        }

        [Fact]
        public void CreateItem_template_has_correct_SpawnRoomBlueprintId()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs, "room.dungeon");
            var result = sys.CreateItem("Scroll", roomId);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("room.dungeon", itemTemplate.SpawnRoomBlueprintId);
        }

        [Fact]
        public void CreateItem_returns_template_reference_in_result()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Ring", roomId);

            Assert.NotNull(result.Template);
            Assert.IsType<ItemTemplate>(result.Template);
        }

        [Fact]
        public void CreateItem_assigns_unique_blueprint_ids_to_successive_items()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var r1 = sys.CreateItem("Sword", roomId);
            var r2 = sys.CreateItem("Shield", roomId);

            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        [Fact]
        public void CreateItem_SpawnRoomBlueprintId_empty_when_room_has_no_BlueprintComponent()
        {
            var (sys, ecs, registry) = Build();
            // Create a room entity with NO BlueprintComponent
            var bareRoomId = ecs.CreateEntity().Id;
            var result = sys.CreateItem("Coin", bareRoomId);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(string.Empty, itemTemplate.SpawnRoomBlueprintId);
        }

        // ── SetItemName ──────────────────────────────────────────────────────────

        [Fact]
        public void SetItemName_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Old Name", roomId);

            sys.SetItemName(result.ItemEntityId, "New Name");

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal("New Name", item.Name);
        }

        [Fact]
        public void SetItemName_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Old Name", roomId);

            sys.SetItemName(result.ItemEntityId, "New Name");

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("New Name", itemTemplate.Name);
        }

        [Fact]
        public void SetItemName_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemName(99999u, "Ghost Item");
        }

        // ── SetItemDescription ───────────────────────────────────────────────────

        [Fact]
        public void SetItemDescription_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Potion", roomId);

            sys.SetItemDescription(result.ItemEntityId, "A vial of red liquid.");

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal("A vial of red liquid.", item.Description);
        }

        [Fact]
        public void SetItemDescription_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Potion", roomId);

            sys.SetItemDescription(result.ItemEntityId, "A vial of red liquid.");

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("A vial of red liquid.", itemTemplate.Description);
        }

        [Fact]
        public void SetItemDescription_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemDescription(99999u, "Nowhere.");
        }

        // ── SetItemKeywords ──────────────────────────────────────────────────────

        [Fact]
        public void SetItemKeywords_updates_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Sword", roomId);

            sys.SetItemKeywords(result.ItemEntityId, new[] { "sword", "blade", "weapon" });

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Contains("sword", item.Keywords);
            Assert.Contains("blade", item.Keywords);
            Assert.Contains("weapon", item.Keywords);
        }

        [Fact]
        public void SetItemKeywords_replaces_existing_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Sword", roomId);

            sys.SetItemKeywords(result.ItemEntityId, new[] { "first" });
            sys.SetItemKeywords(result.ItemEntityId, new[] { "second", "third" });

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.DoesNotContain("first", item.Keywords);
            Assert.Contains("second", item.Keywords);
            Assert.Equal(2, item.Keywords.Count);
        }

        [Fact]
        public void SetItemKeywords_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Axe", roomId);

            sys.SetItemKeywords(result.ItemEntityId, new[] { "axe", "hatchet" });

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Contains("axe", itemTemplate.Keywords);
            Assert.Contains("hatchet", itemTemplate.Keywords);
        }

        [Fact]
        public void SetItemKeywords_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemKeywords(99999u, new[] { "ghost" });
        }

        // ── SetItemType ──────────────────────────────────────────────────────────

        [Fact]
        public void SetItemType_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Longsword", roomId);

            sys.SetItemType(result.ItemEntityId, ItemType.Weapon);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(ItemType.Weapon, item.ItemType);
        }

        [Fact]
        public void SetItemType_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Chainmail", roomId);

            sys.SetItemType(result.ItemEntityId, ItemType.Armor);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(ItemType.Armor, itemTemplate.ItemType);
        }

        [Fact]
        public void SetItemType_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemType(99999u, ItemType.Weapon);
        }

        // ── SetItemSlots ─────────────────────────────────────────────────────────

        [Fact]
        public void SetItemSlots_updates_WornSlots_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Helmet", roomId);

            sys.SetItemSlots(result.ItemEntityId, new[] { WornSlot.Head });

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.NotNull(item.WornSlots);
            Assert.Contains(WornSlot.Head, item.WornSlots!);
        }

        [Fact]
        public void SetItemSlots_empty_list_clears_WornSlots_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Helmet", roomId);

            sys.SetItemSlots(result.ItemEntityId, new[] { WornSlot.Head });
            sys.SetItemSlots(result.ItemEntityId, new List<WornSlot>());

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.True(item.WornSlots == null || item.WornSlots!.Count == 0);
        }

        [Fact]
        public void SetItemSlots_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Breastplate", roomId);

            sys.SetItemSlots(result.ItemEntityId, new[] { WornSlot.Chest });

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Contains(WornSlot.Chest, itemTemplate.WornSlots);
        }

        [Fact]
        public void SetItemSlots_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemSlots(99999u, new[] { WornSlot.Feet });
        }

        // ── SetItemStatBonus / ClearItemStatBonuses (T-U6) ───────────────────────

        [Fact]
        public void SetItemStatBonus_updates_ItemDataComponent_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Broadsword", roomId);

            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 5);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(new EquipmentStatBonus(ScoreId.AttackPower, 5), Assert.Single(item.StatBonuses));

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(new EquipmentStatBonus(ScoreId.AttackPower, 5), Assert.Single(itemTemplate.StatBonuses));
        }

        [Fact]
        public void SetItemStatBonus_replaces_existing_row_for_same_score()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Broadsword", roomId);

            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 5);
            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 9);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(new EquipmentStatBonus(ScoreId.AttackPower, 9), Assert.Single(item.StatBonuses));
        }

        [Fact]
        public void SetItemStatBonus_keeps_distinct_scores_as_separate_rows()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Plate Mail", roomId);

            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.Defense, 4);
            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 2);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(2, item.StatBonuses.Count);
            Assert.Contains(new EquipmentStatBonus(ScoreId.Defense, 4), item.StatBonuses);
            Assert.Contains(new EquipmentStatBonus(ScoreId.AttackPower, 2), item.StatBonuses);
        }

        [Fact]
        public void SetItemStatBonus_with_zero_magnitude_removes_the_row()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Broadsword", roomId);

            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 5);
            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 0);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Empty(item.StatBonuses);
        }

        [Fact]
        public void ClearItemStatBonuses_empties_component_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Plate Mail", roomId);

            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.Defense, 4);
            sys.SetItemStatBonus(result.ItemEntityId, ScoreId.AttackPower, 2);
            sys.ClearItemStatBonuses(result.ItemEntityId);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Empty(item.StatBonuses);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Empty(itemTemplate.StatBonuses);
        }

        [Fact]
        public void SetItemStatBonus_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemStatBonus(99999u, ScoreId.AttackPower, 10);
            sys.ClearItemStatBonuses(99999u);
        }

        // ── INV-5: ItemBuilderSystem does not hold IEventBus ─────────────────────

        [Fact]
        public void ItemBuilderSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(ItemBuilderSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: ItemBuilderSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }

        // ── SetItemValue — dual-write (item-value WP1) ────────────────────────────

        [Fact]
        public void SetItemValue_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Gold Ring", roomId);

            sys.SetItemValue(result.ItemEntityId, 250L);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(250L, item.Value);
        }

        [Fact]
        public void SetItemValue_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Silver Amulet", roomId);

            sys.SetItemValue(result.ItemEntityId, 750L);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(750L, itemTemplate.Value);
        }

        [Fact]
        public void SetItemValue_is_noop_for_unknown_entity()
        {
            // Pure setter — no throw for unknown entity (validation is caller's responsibility).
            var (sys, _, _) = Build();
            sys.SetItemValue(99999u, 100L);
        }

        [Fact]
        public void SetItemValue_zero_is_accepted_as_valueless()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Pebble", roomId);

            sys.SetItemValue(result.ItemEntityId, 500L);
            sys.SetItemValue(result.ItemEntityId, 0L);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(0L, item.Value);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(0L, itemTemplate.Value);
        }

        // ── ItemTemplate.Apply copies Value (item-value WP1) ──────────────────────

        [Fact]
        public void ItemTemplate_Apply_copies_default_zero_Value_onto_ItemDataComponent()
        {
            var ecs = new EntityService();
            var template = new ItemTemplate("item.test.zero") { Name = "Pebble" };
            // Value defaults to 0 — the "valueless" sentinel.
            Assert.Equal(0L, template.Value);

            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            var comp = ecs.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(0L, comp.Value);
        }

        [Fact]
        public void ItemTemplate_Apply_copies_nonzero_Value_onto_ItemDataComponent()
        {
            var ecs = new EntityService();
            var template = new ItemTemplate("item.test.valued")
            {
                Name = "Diamond",
                Value = 5000L,
            };

            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            var comp = ecs.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(5000L, comp.Value);
        }

        // ── SetItemTier / SetItemBand — dual-write (power-model-revision WP-B) ───

        [Fact]
        public void SetItemTier_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Banded Sword", roomId);

            sys.SetItemTier(result.ItemEntityId, 3);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(3, item.Tier);
        }

        [Fact]
        public void SetItemTier_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Banded Shield", roomId);

            sys.SetItemTier(result.ItemEntityId, 4);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(4, itemTemplate.Tier);
        }

        [Fact]
        public void SetItemTier_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemTier(99999u, 2);
        }

        [Fact]
        public void SetItemBand_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Banded Sword", roomId);

            sys.SetItemBand(result.ItemEntityId, 3);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(3, item.Band);
        }

        [Fact]
        public void SetItemBand_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateItem("Banded Shield", roomId);

            sys.SetItemBand(result.ItemEntityId, 2);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(2, itemTemplate.Band);
        }

        [Fact]
        public void SetItemBand_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetItemBand(99999u, 2);
        }
    }
}
