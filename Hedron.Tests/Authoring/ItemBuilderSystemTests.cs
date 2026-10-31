using System.Linq;
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

        private EntityService _ecs = null!;
        private TemplateRegistry _registry = null!;
        private ItemBuilderSystem _sys = null!;

        private (ItemBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = _ecs = new EntityService();
            var registry = _registry = new TemplateRegistry(ecs);
            var system = _sys = new ItemBuilderSystem(ecs, registry, NullLogger<ItemBuilderSystem>.Instance);
            return (system, ecs, registry);
        }

        /// <summary>Creates a room entity with a BlueprintComponent.</summary>
        private static uint MakeRoom(EntityService ecs, string blueprintId = "room.test")
        {
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = blueprintId });
            return room.Id;
        }

        /// <summary>
        /// Registers a item template via <c>CreateItem</c> and spawns one live world instance of it in
        /// <paramref name="roomId"/> — the shape a count-1 room spawn rule produces.
        /// </summary>
        private (uint ItemEntityId, string BlueprintId, ItemTemplate Template) CreateLive(string name, uint roomId)
        {
            var result = _sys.CreateItem(name);
            return (SpawnInstance(result.BlueprintId, roomId), result.BlueprintId, result.Template);
        }

        private uint SpawnInstance(string blueprintId, uint roomId)
        {
            var entity = _registry.Spawn(blueprintId);
            _ecs.AddComponent(entity.Id, new LocationComponent { RoomEntityId = roomId });
            return entity.Id;
        }

        // ── CreateItem (template only — the instance comes from a room spawn rule) ──

        [Fact]
        public void CreateItem_registers_template_with_name_and_creates_no_entity()
        {
            var (sys, ecs, registry) = Build();
            var before = ecs.GetAllComponents<BlueprintComponent>().Count();

            var result = sys.CreateItem("Iron Sword");

            Assert.False(string.IsNullOrWhiteSpace(result.BlueprintId));
            Assert.True(registry.TryGet(result.BlueprintId, out var template));
            var item = Assert.IsType<ItemTemplate>(template);
            Assert.Same(item, result.Template);
            Assert.Equal("Iron Sword", item.Name);
            Assert.Equal(before, ecs.GetAllComponents<BlueprintComponent>().Count());
        }

        [Fact]
        public void CreateItem_assigns_unique_blueprint_ids_to_successive_items()
        {
            var (sys, _, _) = Build();
            var r1 = sys.CreateItem("Sword");
            var r2 = sys.CreateItem("Shield");
            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        // ── Blueprint-keyed fan-out (INV-21) ─────────────────────────────────────

        [Fact]
        public void Set_updates_template_and_every_world_instance_but_never_a_player_owned_copy()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var a = CreateLive("Torch", roomId);
            var b = SpawnInstance(a.BlueprintId, roomId);
            var owned = SpawnInstance(a.BlueprintId, roomId);
            ecs.AddComponent(owned, new PersistentEntity());

            var updated = sys.SetItemValue(a.BlueprintId, 75);

            Assert.Equal(2, updated);
            Assert.Equal(75, a.Template.Value);
            Assert.Equal(75, ecs.Get<ItemDataComponent>(a.ItemEntityId).Value);
            Assert.Equal(75, ecs.Get<ItemDataComponent>(b).Value);
            Assert.NotEqual(75, ecs.Get<ItemDataComponent>(owned).Value);
        }

        [Fact]
        public void Set_with_zero_live_instances_updates_template_and_returns_zero()
        {
            var (sys, _, _) = Build();
            var result = sys.CreateItem("Gem");

            Assert.Equal(0, sys.SetItemName(result.BlueprintId, "Ruby"));
            Assert.Equal("Ruby", result.Template.Name);
        }

        [Fact]
        public void Set_returns_minus_one_for_a_blueprint_that_is_not_an_item_template()
        {
            var (sys, _, _) = Build();
            Assert.Equal(-1, sys.SetItemName("item.unknown", "x"));
        }

        // ── SetItemName ──────────────────────────────────────────────────────────

        [Fact]
        public void SetItemName_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Old Name", roomId);

            sys.SetItemName(result.BlueprintId, "New Name");

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal("New Name", item.Name);
        }

        [Fact]
        public void SetItemName_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Old Name", roomId);

            sys.SetItemName(result.BlueprintId, "New Name");

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("New Name", itemTemplate.Name);
        }

        [Fact]
        public void SetItemName_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemName("item.unknown", "Ghost Item");
        }

        // ── SetItemDescription ───────────────────────────────────────────────────

        [Fact]
        public void SetItemDescription_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Potion", roomId);

            sys.SetItemDescription(result.BlueprintId, "A vial of red liquid.");

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal("A vial of red liquid.", item.Description);
        }

        [Fact]
        public void SetItemDescription_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Potion", roomId);

            sys.SetItemDescription(result.BlueprintId, "A vial of red liquid.");

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal("A vial of red liquid.", itemTemplate.Description);
        }

        [Fact]
        public void SetItemDescription_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemDescription("item.unknown", "Nowhere.");
        }

        // ── SetItemKeywords ──────────────────────────────────────────────────────

        [Fact]
        public void SetItemKeywords_updates_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Sword", roomId);

            sys.SetItemKeywords(result.BlueprintId, new[] { "sword", "blade", "weapon" });

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
            var result = CreateLive("Sword", roomId);

            sys.SetItemKeywords(result.BlueprintId, new[] { "first" });
            sys.SetItemKeywords(result.BlueprintId, new[] { "second", "third" });

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
            var result = CreateLive("Axe", roomId);

            sys.SetItemKeywords(result.BlueprintId, new[] { "axe", "hatchet" });

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Contains("axe", itemTemplate.Keywords);
            Assert.Contains("hatchet", itemTemplate.Keywords);
        }

        [Fact]
        public void SetItemKeywords_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemKeywords("item.unknown", new[] { "ghost" });
        }

        // ── SetItemType ──────────────────────────────────────────────────────────

        [Fact]
        public void SetItemType_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Longsword", roomId);

            sys.SetItemType(result.BlueprintId, ItemType.Weapon);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(ItemType.Weapon, item.ItemType);
        }

        [Fact]
        public void SetItemType_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Chainmail", roomId);

            sys.SetItemType(result.BlueprintId, ItemType.Armor);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(ItemType.Armor, itemTemplate.ItemType);
        }

        [Fact]
        public void SetItemType_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemType("item.unknown", ItemType.Weapon);
        }

        // ── SetItemSlots ─────────────────────────────────────────────────────────

        [Fact]
        public void SetItemSlots_updates_WornSlots_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Helmet", roomId);

            sys.SetItemSlots(result.BlueprintId, new[] { WornSlot.Head });

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.NotNull(item.WornSlots);
            Assert.Contains(WornSlot.Head, item.WornSlots!);
        }

        [Fact]
        public void SetItemSlots_empty_list_clears_WornSlots_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Helmet", roomId);

            sys.SetItemSlots(result.BlueprintId, new[] { WornSlot.Head });
            sys.SetItemSlots(result.BlueprintId, new List<WornSlot>());

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.True(item.WornSlots == null || item.WornSlots!.Count == 0);
        }

        [Fact]
        public void SetItemSlots_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Breastplate", roomId);

            sys.SetItemSlots(result.BlueprintId, new[] { WornSlot.Chest });

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Contains(WornSlot.Chest, itemTemplate.WornSlots);
        }

        [Fact]
        public void SetItemSlots_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemSlots("item.unknown", new[] { WornSlot.Feet });
        }

        // ── SetItemStatBonus / ClearItemStatBonuses (T-U6) ───────────────────────

        [Fact]
        public void SetItemStatBonus_updates_ItemDataComponent_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Broadsword", roomId);

            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 5);

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
            var result = CreateLive("Broadsword", roomId);

            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 5);
            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 9);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(new EquipmentStatBonus(ScoreId.AttackPower, 9), Assert.Single(item.StatBonuses));
        }

        [Fact]
        public void SetItemStatBonus_keeps_distinct_scores_as_separate_rows()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Plate Mail", roomId);

            sys.SetItemStatBonus(result.BlueprintId, ScoreId.Defense, 4);
            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 2);

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
            var result = CreateLive("Broadsword", roomId);

            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 5);
            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 0);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Empty(item.StatBonuses);
        }

        [Fact]
        public void ClearItemStatBonuses_empties_component_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Plate Mail", roomId);

            sys.SetItemStatBonus(result.BlueprintId, ScoreId.Defense, 4);
            sys.SetItemStatBonus(result.BlueprintId, ScoreId.AttackPower, 2);
            sys.ClearItemStatBonuses(result.BlueprintId);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Empty(item.StatBonuses);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Empty(itemTemplate.StatBonuses);
        }

        [Fact]
        public void SetItemStatBonus_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemStatBonus("item.unknown", ScoreId.AttackPower, 10);
            sys.ClearItemStatBonuses("item.unknown");
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
            var result = CreateLive("Gold Ring", roomId);

            sys.SetItemValue(result.BlueprintId, 250L);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(250L, item.Value);
        }

        [Fact]
        public void SetItemValue_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Silver Amulet", roomId);

            sys.SetItemValue(result.BlueprintId, 750L);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(750L, itemTemplate.Value);
        }

        [Fact]
        public void SetItemValue_is_noop_for_unknown_blueprint()
        {
            // Pure setter — no throw for unknown entity (validation is caller's responsibility).
            var (sys, _, _) = Build();
            sys.SetItemValue("item.unknown", 100L);
        }

        [Fact]
        public void SetItemValue_zero_is_accepted_as_valueless()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Pebble", roomId);

            sys.SetItemValue(result.BlueprintId, 500L);
            sys.SetItemValue(result.BlueprintId, 0L);

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
            var result = CreateLive("Banded Sword", roomId);

            sys.SetItemTier(result.BlueprintId, 3);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(3, item.Tier);
        }

        [Fact]
        public void SetItemTier_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Banded Shield", roomId);

            sys.SetItemTier(result.BlueprintId, 4);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(4, itemTemplate.Tier);
        }

        [Fact]
        public void SetItemTier_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemTier("item.unknown", 2);
        }

        [Fact]
        public void SetItemBand_updates_ItemDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Banded Sword", roomId);

            sys.SetItemBand(result.BlueprintId, 3);

            var item = ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(3, item.Band);
        }

        [Fact]
        public void SetItemBand_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Banded Shield", roomId);

            sys.SetItemBand(result.BlueprintId, 2);

            registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(2, itemTemplate.Band);
        }

        [Fact]
        public void SetItemBand_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetItemBand("item.unknown", 2);
        }
    }
}
