using System.Linq;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="MobBuilderSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/mobs.md
    /// (authoring / builder section) and the interface <see cref="IMobBuilderSystem"/>.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>
    /// (no mocking framework).
    /// </summary>
    public sealed class MobBuilderSystemTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private EntityService _ecs = null!;
        private TemplateRegistry _registry = null!;
        private MobBuilderSystem _sys = null!;

        private (MobBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = _ecs = new EntityService();
            var registry = _registry = new TemplateRegistry(ecs);
            var system = _sys = new MobBuilderSystem(ecs, registry, NullLogger<MobBuilderSystem>.Instance);
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
        /// Registers a mob template via <c>CreateMob</c> and spawns one live world instance of it in
        /// <paramref name="roomId"/> — the shape a count-1 room spawn rule produces.
        /// </summary>
        private (uint MobEntityId, string BlueprintId, MobTemplate Template) CreateLive(string name, uint roomId)
        {
            var result = _sys.CreateMob(name);
            return (SpawnInstance(result.BlueprintId, roomId), result.BlueprintId, result.Template);
        }

        private uint SpawnInstance(string blueprintId, uint roomId)
        {
            var entity = _registry.Spawn(blueprintId);
            _ecs.AddComponent(entity.Id, new LocationComponent { RoomEntityId = roomId });
            return entity.Id;
        }

        // ── CreateMob (template only — the instance comes from a room spawn rule) ──

        [Fact]
        public void CreateMob_registers_template_with_name_and_creates_no_entity()
        {
            var (sys, ecs, registry) = Build();
            var before = ecs.GetAllComponents<BlueprintComponent>().Count();

            var result = sys.CreateMob("Goblin");

            Assert.False(string.IsNullOrWhiteSpace(result.BlueprintId));
            Assert.True(registry.TryGet(result.BlueprintId, out var template));
            var mob = Assert.IsType<MobTemplate>(template);
            Assert.Same(mob, result.Template);
            Assert.Equal("Goblin", mob.Name);
            Assert.Equal(before, ecs.GetAllComponents<BlueprintComponent>().Count());
        }

        [Fact]
        public void CreateMob_assigns_unique_blueprint_ids_to_successive_mobs()
        {
            var (sys, _, _) = Build();
            var r1 = sys.CreateMob("Goblin A");
            var r2 = sys.CreateMob("Goblin B");
            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        // ── Blueprint-keyed fan-out (INV-21) ─────────────────────────────────────

        [Fact]
        public void Set_updates_template_and_every_world_instance_but_never_a_persistent_copy()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var a = CreateLive("Rat", roomId);
            var b = SpawnInstance(a.BlueprintId, roomId);
            var c = SpawnInstance(a.BlueprintId, roomId);
            var owned = SpawnInstance(a.BlueprintId, roomId);
            ecs.AddComponent(owned, new PersistentEntity());

            var updated = sys.SetAttribute(a.BlueprintId, "hp", 30);

            Assert.Equal(3, updated);
            Assert.Equal(30, a.Template.MaxHp);
            foreach (var id in new[] { a.MobEntityId, b, c })
                Assert.Equal(30, ecs.Get<PoolsComponent>(id).MaxHp);
            Assert.NotEqual(30, ecs.Get<PoolsComponent>(owned).MaxHp);
        }

        [Fact]
        public void Set_with_zero_live_instances_updates_template_and_returns_zero()
        {
            var (sys, _, registry) = Build();
            var result = sys.CreateMob("Ghost");

            Assert.Equal(0, sys.SetMobName(result.BlueprintId, "Wraith"));
            Assert.Equal("Wraith", result.Template.Name);
        }

        [Fact]
        public void Set_returns_minus_one_for_a_blueprint_that_is_not_a_mob_template()
        {
            var (sys, _, _) = Build();
            Assert.Equal(-1, sys.SetMobName("mob.unknown", "x"));
        }

        // ── SetMobName ───────────────────────────────────────────────────────────

        [Fact]
        public void SetMobName_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Old Name", roomId);

            sys.SetMobName(result.BlueprintId, "New Name");

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal("New Name", mob.Name);
        }

        [Fact]
        public void SetMobName_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Old Name", roomId);

            sys.SetMobName(result.BlueprintId, "New Name");

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("New Name", mobTemplate.Name);
        }

        [Fact]
        public void SetMobName_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobName("mob.unknown", "Ghost Mob");
        }

        // ── SetMobDescription ────────────────────────────────────────────────────

        [Fact]
        public void SetMobDescription_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Orc", roomId);

            sys.SetMobDescription(result.BlueprintId, "A hulking green creature.");

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal("A hulking green creature.", mob.Description);
        }

        [Fact]
        public void SetMobDescription_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Orc", roomId);

            sys.SetMobDescription(result.BlueprintId, "A hulking green creature.");

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("A hulking green creature.", mobTemplate.Description);
        }

        [Fact]
        public void SetMobDescription_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobDescription("mob.unknown", "Nowhere.");
        }

        // ── SetMobKeywords ───────────────────────────────────────────────────────

        [Fact]
        public void SetMobKeywords_updates_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Goblin", roomId);

            sys.SetMobKeywords(result.BlueprintId, new[] { "goblin", "creature", "green" });

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Contains("goblin", mob.Keywords);
            Assert.Contains("creature", mob.Keywords);
            Assert.Contains("green", mob.Keywords);
        }

        [Fact]
        public void SetMobKeywords_replaces_existing_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Goblin", roomId);

            sys.SetMobKeywords(result.BlueprintId, new[] { "first" });
            sys.SetMobKeywords(result.BlueprintId, new[] { "second", "third" });

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.DoesNotContain("first", mob.Keywords);
            Assert.Contains("second", mob.Keywords);
            Assert.Equal(2, mob.Keywords.Count);
        }

        [Fact]
        public void SetMobKeywords_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Troll", roomId);

            sys.SetMobKeywords(result.BlueprintId, new[] { "troll", "giant" });

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Contains("troll", mobTemplate.Keywords);
            Assert.Contains("giant", mobTemplate.Keywords);
        }

        [Fact]
        public void SetMobKeywords_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobKeywords("mob.unknown", new[] { "ghost" });
        }

        // ── SetMobType ───────────────────────────────────────────────────────────

        [Fact]
        public void SetMobType_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            sys.SetMobType(result.BlueprintId, MobType.Vendor);

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(MobType.Vendor, mob.MobType);
        }

        [Fact]
        public void SetMobType_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("City Guard", roomId);

            sys.SetMobType(result.BlueprintId, MobType.Guard);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(MobType.Guard, mobTemplate.MobType);
        }

        [Fact]
        public void SetMobType_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobType("mob.unknown", MobType.Creature);
        }

        // ── SetAttribute ─────────────────────────────────────────────────────────

        [Fact]
        public void SetAttribute_level_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Veteran", roomId);

            sys.SetAttribute(result.BlueprintId, "level", 10);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(10, attr.Level);
            Assert.Equal(10, result.Template.Level);
        }

        [Fact]
        public void SetAttribute_hp_updates_PoolsComponent_MaxHp_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Tank", roomId);

            sys.SetAttribute(result.BlueprintId, "hp", 200);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(200, pools.MaxHp);
            Assert.Equal(200, result.Template.MaxHp);
        }

        [Fact]
        public void SetAttribute_hp_clamps_CurrentHp_to_new_MaxHp_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Wounded", roomId);

            // First raise HP high so CurrentHp is elevated, then reduce max below it.
            sys.SetAttribute(result.BlueprintId, "hp", 100);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentHp = 100; // simulate full health at 100

            sys.SetAttribute(result.BlueprintId, "hp", 50);

            Assert.Equal(50, pools.MaxHp);
            Assert.Equal(50, pools.CurrentHp); // clamped to new max
        }

        [Fact]
        public void SetAttribute_mind_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Mage", roomId);

            sys.SetAttribute(result.BlueprintId, "mind", 18);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(18, attr.Mind);
            Assert.Equal(18, result.Template.Mind);
        }

        [Fact]
        public void SetAttribute_body_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Brute", roomId);

            sys.SetAttribute(result.BlueprintId, "body", 20);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(20, attr.Body);
            Assert.Equal(20, result.Template.Body);
        }

        [Fact]
        public void SetAttribute_spirit_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Priest", roomId);

            sys.SetAttribute(result.BlueprintId, "spirit", 15);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(15, attr.Spirit);
            Assert.Equal(15, result.Template.Spirit);
        }

        [Fact]
        public void SetAttribute_attunement_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Shaman", roomId);

            sys.SetAttribute(result.BlueprintId, "attunement", 14);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(14, attr.Attunement);
            Assert.Equal(14, result.Template.Attunement);
        }

        [Fact]
        public void SetAttribute_maxmana_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Warlock", roomId);

            sys.SetAttribute(result.BlueprintId, "maxmana", 120);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(120, pools.MaxMana);
            Assert.Equal(120, result.Template.MaxMana);
        }

        [Fact]
        public void SetAttribute_maxmana_clamps_CurrentMana_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Caster", roomId);

            sys.SetAttribute(result.BlueprintId, "maxmana", 80);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentMana = 80;

            sys.SetAttribute(result.BlueprintId, "maxmana", 30);

            Assert.Equal(30, pools.MaxMana);
            Assert.Equal(30, pools.CurrentMana);
        }

        [Fact]
        public void SetAttribute_maxstamina_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Runner", roomId);

            sys.SetAttribute(result.BlueprintId, "maxstamina", 90);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(90, pools.MaxStamina);
            Assert.Equal(90, result.Template.MaxStamina);
        }

        [Fact]
        public void SetAttribute_maxstamina_clamps_CurrentStamina_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Tired", roomId);

            sys.SetAttribute(result.BlueprintId, "maxstamina", 60);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentStamina = 60;

            sys.SetAttribute(result.BlueprintId, "maxstamina", 20);

            Assert.Equal(20, pools.MaxStamina);
            Assert.Equal(20, pools.CurrentStamina);
        }

        [Fact]
        public void SetAttribute_maxastra_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Astral", roomId);

            sys.SetAttribute(result.BlueprintId, "maxastra", 40);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(40, pools.MaxAstra);
            Assert.Equal(40, result.Template.MaxAstra);
        }

        [Fact]
        public void SetAttribute_maxastra_clamps_CurrentAstra_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Drained", roomId);

            sys.SetAttribute(result.BlueprintId, "maxastra", 20);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentAstra = 20;

            sys.SetAttribute(result.BlueprintId, "maxastra", 5);

            Assert.Equal(5, pools.MaxAstra);
            Assert.Equal(5, pools.CurrentAstra);
        }

        [Fact]
        public void SetAttribute_unknown_property_is_ignored_and_does_not_throw()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Test", roomId);

            // Unknown property key — should be a no-op (switch default falls through)
            sys.SetAttribute(result.BlueprintId, "nonsense", 42);
        }

        // ── SetMobProtection ─────────────────────────────────────────────────────

        [Fact]
        public void SetMobProtection_Untargetable_adds_ProtectionComponent_with_correct_flags()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Shopkeeper", roomId);

            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.Untargetable);

            Assert.True(ecs.HasComponent<ProtectionComponent>(result.MobEntityId));
            var comp = ecs.Get<ProtectionComponent>(result.MobEntityId);
            Assert.Equal(ProtectionFlags.Untargetable, comp.Flags);
        }

        [Fact]
        public void SetMobProtection_BothFlags_adds_ProtectionComponent_with_both_flags()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Guard", roomId);

            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune);

            var comp = ecs.Get<ProtectionComponent>(result.MobEntityId);
            Assert.Equal(ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune, comp.Flags);
        }

        [Fact]
        public void SetMobProtection_updates_template_Protection_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Vendor", roomId);

            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune, mobTemplate.Protection);
        }

        [Fact]
        public void SetMobProtection_None_removes_existing_ProtectionComponent()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Mob", roomId);

            // First set a flag...
            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.Untargetable);
            Assert.True(ecs.HasComponent<ProtectionComponent>(result.MobEntityId));

            // ...then clear it to None — component should be removed.
            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.None);
            Assert.False(ecs.HasComponent<ProtectionComponent>(result.MobEntityId));
        }

        [Fact]
        public void SetMobProtection_None_sets_template_Protection_to_None()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Mob", roomId);

            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.EffectImmune);
            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.None);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(ProtectionFlags.None, mobTemplate.Protection);
        }

        [Fact]
        public void SetMobProtection_updates_existing_ProtectionComponent_flags()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Mob", roomId);

            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.Untargetable);
            sys.SetMobProtection(result.BlueprintId, ProtectionFlags.EffectImmune);

            var comp = ecs.Get<ProtectionComponent>(result.MobEntityId);
            Assert.Equal(ProtectionFlags.EffectImmune, comp.Flags);
        }

        [Fact]
        public void SetMobProtection_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            // Should not throw even if entity doesn't exist.
            sys.SetMobProtection("mob.unknown", ProtectionFlags.Untargetable);
        }

        // ── SetMobTier / SetMobBand ────────────────────────────────────────────────

        [Fact]
        public void SetMobTier_dual_writes_live_component_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Trash", roomId);

            sys.SetMobTier(result.BlueprintId, 2);

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(2, mob.Tier);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(2, mobTemplate.Tier);
        }

        [Fact]
        public void SetMobTier_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobTier("mob.unknown", 3);
        }

        [Fact]
        public void SetMobBand_dual_writes_live_component_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Trash", roomId);

            sys.SetMobBand(result.BlueprintId, 2);

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(2, mob.Band);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(2, mobTemplate.Band);
        }

        [Fact]
        public void SetMobBand_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            sys.SetMobBand("mob.unknown", 3);
        }

        // ── SetMobShop ───────────────────────────────────────────────────────────

        [Fact]
        public void SetMobShop_true_adds_ShopComponent_and_Inventory_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 5000);

            Assert.True(ecs.HasComponent<ShopComponent>(result.MobEntityId));
            Assert.True(ecs.HasComponent<InventoryComponent>(result.MobEntityId));
            var shop = ecs.Get<ShopComponent>(result.MobEntityId);
            Assert.Equal(CurrencyId.Coin, shop.AcceptedCurrency);
            Assert.Equal(5000, shop.TillSeed);
        }

        [Fact]
        public void SetMobShop_true_dual_writes_template_shop_fields()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 5000);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.True(mobTemplate.IsShop);
            Assert.Equal(CurrencyId.Coin, mobTemplate.ShopAcceptedCurrency);
            Assert.Equal(5000, mobTemplate.ShopTillSeed);
        }

        [Fact]
        public void SetMobShop_true_with_baseStock_populates_BaseStock_on_entity_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            var rows = new[]
            {
                new ShopStockRow { BlueprintId = "item.sword", Quantity = 2 },
                new ShopStockRow { BlueprintId = "item.potion", Quantity = 5 },
            };
            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 100, baseStock: rows);

            var shop = ecs.Get<ShopComponent>(result.MobEntityId);
            Assert.Equal(2, shop.BaseStock.Count);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(2, mobTemplate.ShopBaseStock.Count);
        }

        [Fact]
        public void SetMobShop_true_with_null_baseStock_leaves_existing_BaseStock_unchanged()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            var rows = new[] { new ShopStockRow { BlueprintId = "item.sword", Quantity = 1 } };
            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 100, baseStock: rows);

            // Update only the till seed; pass null base stock — existing rows must survive.
            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 999, baseStock: null);

            var shop = ecs.Get<ShopComponent>(result.MobEntityId);
            Assert.Single(shop.BaseStock);
            Assert.Equal(999, shop.TillSeed);
        }

        [Fact]
        public void SetMobShop_false_removes_ShopComponent_and_clears_template_fields()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = CreateLive("Merchant", roomId);

            var rows = new[] { new ShopStockRow { BlueprintId = "item.sword", Quantity = 1 } };
            sys.SetMobShop(result.BlueprintId, isShop: true, CurrencyId.Coin, tillSeed: 100, baseStock: rows);
            Assert.True(ecs.HasComponent<ShopComponent>(result.MobEntityId));

            sys.SetMobShop(result.BlueprintId, isShop: false);

            Assert.False(ecs.HasComponent<ShopComponent>(result.MobEntityId));
            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.False(mobTemplate.IsShop);
            Assert.Empty(mobTemplate.ShopBaseStock);
        }

        [Fact]
        public void SetMobShop_false_is_noop_for_unknown_blueprint()
        {
            var (sys, _, _) = Build();
            // Should not throw even if entity doesn't exist.
            sys.SetMobShop("mob.unknown", isShop: false);
        }

        // ── INV-5: MobBuilderSystem does not hold IEventBus ──────────────────────

        [Fact]
        public void MobBuilderSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(MobBuilderSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: MobBuilderSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }
    }
}
