using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
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

        private static (MobBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new MobBuilderSystem(ecs, registry, NullLogger<MobBuilderSystem>.Instance);
            return (system, ecs, registry);
        }

        /// <summary>Creates a room entity with a BlueprintComponent so CreateMob can derive SpawnRoomBlueprintId.</summary>
        private static uint MakeRoom(EntityService ecs, string blueprintId = "room.test")
        {
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = blueprintId });
            return room.Id;
        }

        // ── CreateMob ────────────────────────────────────────────────────────────

        [Fact]
        public void CreateMob_returns_nonzero_MobEntityId()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Goblin", roomId);
            Assert.NotEqual(0u, result.MobEntityId);
        }

        [Fact]
        public void CreateMob_returns_nonempty_BlueprintId()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Goblin", roomId);
            Assert.False(string.IsNullOrWhiteSpace(result.BlueprintId));
        }

        [Fact]
        public void CreateMob_attaches_MobDataComponent_with_correct_name()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Orc Warrior", roomId);

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal("Orc Warrior", mob.Name);
        }

        [Fact]
        public void CreateMob_attaches_BlueprintComponent_with_matching_id()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Troll", roomId);

            var bp = ecs.Get<BlueprintComponent>(result.MobEntityId);
            Assert.Equal(result.BlueprintId, bp.BlueprintId);
        }

        [Fact]
        public void CreateMob_attaches_LocationComponent_pointing_to_room()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Spider", roomId);

            var loc = ecs.Get<LocationComponent>(result.MobEntityId);
            Assert.Equal(roomId, loc.RoomEntityId);
        }

        [Fact]
        public void CreateMob_LocationComponent_copies_SpawnRoomBlueprintId_from_room()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs, "room.dungeon");
            var result = sys.CreateMob("Skeleton", roomId);

            var loc = ecs.Get<LocationComponent>(result.MobEntityId);
            Assert.Equal("room.dungeon", loc.RoomBlueprintId);
        }

        [Fact]
        public void CreateMob_attaches_AttributesComponent()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Bandit", roomId);

            Assert.True(ecs.HasComponent<AttributesComponent>(result.MobEntityId));
        }

        [Fact]
        public void CreateMob_attaches_PoolsComponent()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Wolf", roomId);

            Assert.True(ecs.HasComponent<PoolsComponent>(result.MobEntityId));
        }

        [Fact]
        public void CreateMob_registers_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Rat", roomId);

            var found = registry.TryGet(result.BlueprintId, out var template);
            Assert.True(found);
            Assert.NotNull(template);
        }

        [Fact]
        public void CreateMob_template_has_correct_name()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Dragon", roomId);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("Dragon", mobTemplate.Name);
        }

        [Fact]
        public void CreateMob_template_has_correct_SpawnRoomBlueprintId()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs, "room.keep");
            var result = sys.CreateMob("Guard", roomId);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("room.keep", mobTemplate.SpawnRoomBlueprintId);
        }

        [Fact]
        public void CreateMob_returns_template_reference_in_result()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Imp", roomId);

            Assert.NotNull(result.Template);
            Assert.IsType<MobTemplate>(result.Template);
        }

        [Fact]
        public void CreateMob_assigns_unique_blueprint_ids_to_successive_mobs()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var r1 = sys.CreateMob("Goblin A", roomId);
            var r2 = sys.CreateMob("Goblin B", roomId);

            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        [Fact]
        public void CreateMob_SpawnRoomBlueprintId_empty_when_room_has_no_BlueprintComponent()
        {
            var (sys, ecs, registry) = Build();
            var bareRoomId = ecs.CreateEntity().Id; // no BlueprintComponent
            var result = sys.CreateMob("Ghost", bareRoomId);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(string.Empty, mobTemplate.SpawnRoomBlueprintId);
        }

        // ── SetMobName ───────────────────────────────────────────────────────────

        [Fact]
        public void SetMobName_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Old Name", roomId);

            sys.SetMobName(result.MobEntityId, "New Name");

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal("New Name", mob.Name);
        }

        [Fact]
        public void SetMobName_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Old Name", roomId);

            sys.SetMobName(result.MobEntityId, "New Name");

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("New Name", mobTemplate.Name);
        }

        [Fact]
        public void SetMobName_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetMobName(99999u, "Ghost Mob");
        }

        // ── SetMobDescription ────────────────────────────────────────────────────

        [Fact]
        public void SetMobDescription_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Orc", roomId);

            sys.SetMobDescription(result.MobEntityId, "A hulking green creature.");

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal("A hulking green creature.", mob.Description);
        }

        [Fact]
        public void SetMobDescription_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Orc", roomId);

            sys.SetMobDescription(result.MobEntityId, "A hulking green creature.");

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal("A hulking green creature.", mobTemplate.Description);
        }

        [Fact]
        public void SetMobDescription_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetMobDescription(99999u, "Nowhere.");
        }

        // ── SetMobKeywords ───────────────────────────────────────────────────────

        [Fact]
        public void SetMobKeywords_updates_keywords_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Goblin", roomId);

            sys.SetMobKeywords(result.MobEntityId, new[] { "goblin", "creature", "green" });

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
            var result = sys.CreateMob("Goblin", roomId);

            sys.SetMobKeywords(result.MobEntityId, new[] { "first" });
            sys.SetMobKeywords(result.MobEntityId, new[] { "second", "third" });

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
            var result = sys.CreateMob("Troll", roomId);

            sys.SetMobKeywords(result.MobEntityId, new[] { "troll", "giant" });

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Contains("troll", mobTemplate.Keywords);
            Assert.Contains("giant", mobTemplate.Keywords);
        }

        [Fact]
        public void SetMobKeywords_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetMobKeywords(99999u, new[] { "ghost" });
        }

        // ── SetMobType ───────────────────────────────────────────────────────────

        [Fact]
        public void SetMobType_updates_MobDataComponent_on_live_entity()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Merchant", roomId);

            sys.SetMobType(result.MobEntityId, MobType.Vendor);

            var mob = ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(MobType.Vendor, mob.MobType);
        }

        [Fact]
        public void SetMobType_updates_template_in_registry()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("City Guard", roomId);

            sys.SetMobType(result.MobEntityId, MobType.Guard);

            registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(MobType.Guard, mobTemplate.MobType);
        }

        [Fact]
        public void SetMobType_is_noop_for_unknown_entity()
        {
            var (sys, _, _) = Build();
            sys.SetMobType(99999u, MobType.Creature);
        }

        // ── SetAttribute ─────────────────────────────────────────────────────────

        [Fact]
        public void SetAttribute_level_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Veteran", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "level", 10);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(10, attr.Level);
            Assert.Equal(10, result.Template.Level);
        }

        [Fact]
        public void SetAttribute_hp_updates_PoolsComponent_MaxHp_and_template()
        {
            var (sys, ecs, registry) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Tank", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "hp", 200);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(200, pools.MaxHp);
            Assert.Equal(200, result.Template.MaxHp);
        }

        [Fact]
        public void SetAttribute_hp_clamps_CurrentHp_to_new_MaxHp_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Wounded", roomId);

            // First raise HP high so CurrentHp is elevated, then reduce max below it.
            sys.SetAttribute(result.MobEntityId, result.Template, "hp", 100);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentHp = 100; // simulate full health at 100

            sys.SetAttribute(result.MobEntityId, result.Template, "hp", 50);

            Assert.Equal(50, pools.MaxHp);
            Assert.Equal(50, pools.CurrentHp); // clamped to new max
        }

        [Fact]
        public void SetAttribute_mind_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Mage", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "mind", 18);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(18, attr.Mind);
            Assert.Equal(18, result.Template.Mind);
        }

        [Fact]
        public void SetAttribute_body_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Brute", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "body", 20);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(20, attr.Body);
            Assert.Equal(20, result.Template.Body);
        }

        [Fact]
        public void SetAttribute_spirit_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Priest", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "spirit", 15);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(15, attr.Spirit);
            Assert.Equal(15, result.Template.Spirit);
        }

        [Fact]
        public void SetAttribute_attunement_updates_AttributesComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Shaman", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "attunement", 14);

            var attr = ecs.Get<AttributesComponent>(result.MobEntityId);
            Assert.Equal(14, attr.Attunement);
            Assert.Equal(14, result.Template.Attunement);
        }

        [Fact]
        public void SetAttribute_maxmana_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Warlock", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxmana", 120);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(120, pools.MaxMana);
            Assert.Equal(120, result.Template.MaxMana);
        }

        [Fact]
        public void SetAttribute_maxmana_clamps_CurrentMana_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Caster", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxmana", 80);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentMana = 80;

            sys.SetAttribute(result.MobEntityId, result.Template, "maxmana", 30);

            Assert.Equal(30, pools.MaxMana);
            Assert.Equal(30, pools.CurrentMana);
        }

        [Fact]
        public void SetAttribute_maxstamina_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Runner", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxstamina", 90);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(90, pools.MaxStamina);
            Assert.Equal(90, result.Template.MaxStamina);
        }

        [Fact]
        public void SetAttribute_maxstamina_clamps_CurrentStamina_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Tired", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxstamina", 60);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentStamina = 60;

            sys.SetAttribute(result.MobEntityId, result.Template, "maxstamina", 20);

            Assert.Equal(20, pools.MaxStamina);
            Assert.Equal(20, pools.CurrentStamina);
        }

        [Fact]
        public void SetAttribute_maxastra_updates_PoolsComponent_and_template()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Astral", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxastra", 40);

            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            Assert.Equal(40, pools.MaxAstra);
            Assert.Equal(40, result.Template.MaxAstra);
        }

        [Fact]
        public void SetAttribute_maxastra_clamps_CurrentAstra_to_new_max_when_lower()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Drained", roomId);

            sys.SetAttribute(result.MobEntityId, result.Template, "maxastra", 20);
            var pools = ecs.Get<PoolsComponent>(result.MobEntityId);
            pools.CurrentAstra = 20;

            sys.SetAttribute(result.MobEntityId, result.Template, "maxastra", 5);

            Assert.Equal(5, pools.MaxAstra);
            Assert.Equal(5, pools.CurrentAstra);
        }

        [Fact]
        public void SetAttribute_unknown_property_is_ignored_and_does_not_throw()
        {
            var (sys, ecs, _) = Build();
            var roomId = MakeRoom(ecs);
            var result = sys.CreateMob("Test", roomId);

            // Unknown property key — should be a no-op (switch default falls through)
            sys.SetAttribute(result.MobEntityId, result.Template, "nonsense", 42);
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
