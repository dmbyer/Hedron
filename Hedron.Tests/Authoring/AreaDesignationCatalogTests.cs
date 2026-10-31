using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — the web-host half of area-designation (P6, P11, P12) over a temp content directory:
    /// catalog rule-save warnings (new/changed rules only), the catalog-backed
    /// <see cref="AreaDesignationAudit"/>, <see cref="CatalogPlacementSource"/>'s tolerance of
    /// unreadable files, and registry/catalog source parity.
    /// </summary>
    public sealed class AreaDesignationCatalogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hedron-designation-" + Guid.NewGuid().ToString("N"));
        private readonly ContentDefinitionCatalog _catalog;

        private sealed class StubAbilityRegistry : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry() : base(Array.Empty<AbilityDefinition>(), d => d.Id) { }
        }

        private sealed class StubEffectRegistry : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry() : base(Array.Empty<EffectDefinition>(), d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry() : base(Array.Empty<AspectDefinition>(), d => d.Id) { }
        }

        private Microsoft.Extensions.Options.IOptions<WorldOptions> Options =>
            Microsoft.Extensions.Options.Options.Create(new WorldOptions { ContentDirectory = _dir, StartingRoomBlueprintId = "room.glade" });

        private static YamlContentSerializer Serializer() => new(new ITemplateDeserializer[]
        {
            new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
            new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
            new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
            new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
        });

        public AreaDesignationCatalogTests()
        {
            Directory.CreateDirectory(_dir);
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var validator = new ContentValidator(
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs, registry);
            _catalog = new ContentDefinitionCatalog(
                Serializer(), validator, registry,
                new AreaContentWriter(Options), new RoomContentWriter(Options),
                new ItemContentWriter(Options), new MobContentWriter(Options),
                Options, NullLogger<ContentDefinitionCatalog>.Instance);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }

        // ── Content on disk ──────────────────────────────────────────────────────

        private void Write(string subdir, string id, string body)
        {
            Directory.CreateDirectory(Path.Combine(_dir, subdir));
            File.WriteAllText(Path.Combine(_dir, subdir, id + ".yaml"), body);
            _catalog.Invalidate();
        }

        private void Area(string id, string extra = "") => Write("areas", id, $"id: {id}\nname: {id}\n{extra}");
        private void Mob(string id, int tier, int band) => Write("mobs", id, $"blueprintId: {id}\nname: {id}\nlevel: 1\ntier: {tier}\nband: {band}\n");
        private void Item(string id, int tier, int band) => Write("items", id, $"blueprintId: {id}\nname: {id}\ntier: {tier}\nband: {band}\n");

        private void Room(string id, string areaId, params (string Id, string Blueprint)[] rules)
        {
            var body = $"id: {id}\nname: {id}\nareaId: {areaId}\n";
            if (rules.Length > 0)
                body += "spawnRules:\n" + string.Concat(rules.Select(r =>
                    $"  - id: {r.Id}\n    blueprintId: {r.Blueprint}\n    count: 1\n    respawnDelaySeconds: 60\n"));
            Write("rooms", id, body);
        }

        private const string B1 = "designation:\n  tier: 0\n  bandMin: 1\n  bandMax: 1\n";

        private RoomTemplate LoadRoom(string id) => (RoomTemplate)_catalog.Load(ContentKind.Room, id)!.Template;

        private static IEnumerable<string> DesignationWarnings(ContentWriteResult result) =>
            result.Warnings.Where(w => w.Contains("outside area"));

        private AreaDesignationAudit Audit() =>
            new(_catalog, _catalog, NullLogger<AreaDesignationAudit>.Instance);

        // ── Rule-save warnings (P11) ─────────────────────────────────────────────

        [Fact]
        public async Task Upsert_of_an_out_of_designation_rule_writes_succeeds_and_warns()
        {
            Area("area.wilds", B1);
            Mob("mob.b3", 0, 3);
            Room("room.glade", "area.wilds");

            var result = await _catalog.UpsertSpawnRuleAsync("room.glade", new SpawnRule("ogres", "mob.b3", 1, 60));

            Assert.True(result.Success);
            Assert.Contains(LoadRoom("room.glade").SpawnRules, r => r.Id == "ogres");
            Assert.Contains("ogres", Assert.Single(DesignationWarnings(result)));
        }

        [Fact]
        public async Task Save_with_unchanged_rules_warns_nothing_even_after_an_area_move()
        {
            Area("area.free");
            Area("area.wilds", B1);
            Mob("mob.b3", 0, 3);
            Room("room.glade", "area.free", ("ogres", "mob.b3"));

            var room = LoadRoom("room.glade");
            room.Name = "Renamed";
            room.AreaId = "area.wilds"; // now out of designation, but the rule itself is unchanged
            var result = await _catalog.SaveAsync(new ContentDefinition(ContentKind.Room, room));

            Assert.True(result.Success);
            Assert.Empty(DesignationWarnings(result));
            Assert.Single(Audit().Findings("area.wilds")); // the area view still shows it
        }

        [Fact]
        public async Task SaveRoom_warns_exactly_the_changed_rule()
        {
            Area("area.wilds", B1);
            Mob("mob.b3", 0, 3);
            Room("room.glade", "area.wilds", ("old-ogres", "mob.b3"), ("rats", "mob.b3"));

            var room = LoadRoom("room.glade");
            var index = room.SpawnRules.FindIndex(r => r.Id == "rats");
            room.SpawnRules[index] = room.SpawnRules[index] with { Count = 2 };
            var result = await _catalog.SaveRoomAsync(room, bidirectional: false);

            Assert.True(result.Success);
            Assert.Contains("'rats'", Assert.Single(DesignationWarnings(result)));
        }

        [Fact]
        public async Task Case_only_edit_of_a_rule_counts_as_unchanged()
        {
            Area("area.wilds", B1);
            Mob("mob.b3", 0, 3);
            Room("room.glade", "area.wilds", ("ogres", "mob.b3"));

            var room = LoadRoom("room.glade");
            room.SpawnRules[0] = room.SpawnRules[0] with { Id = "OGRES", BlueprintId = "MOB.B3" };
            var result = await _catalog.SaveRoomAsync(room, bidirectional: false);

            Assert.True(result.Success);
            Assert.Empty(DesignationWarnings(result));
        }

        // ── Catalog-backed audit (P12) ───────────────────────────────────────────

        [Fact]
        public async Task Audit_reads_disk_filters_by_area_and_sees_a_designation_saved_without_restart()
        {
            Area("area.wilds");
            Area("area.other", B1);
            Mob("mob.b3", 0, 3);
            Item("item.b2", 0, 2);
            Room("room.glade", "area.wilds", ("ogres", "mob.b3"), ("loot", "item.b2"));
            Room("room.cave", "area.other", ("loot", "item.b2"));

            Assert.Equal("room.cave", Assert.Single(Audit().Findings()).RoomBlueprintId);

            var wilds = (AreaTemplate)_catalog.Load(ContentKind.Area, "area.wilds")!.Template;
            wilds.Designation = AreaDesignation.Of(0, 1, 1);
            Assert.True((await _catalog.SaveAsync(new ContentDefinition(ContentKind.Area, wilds))).Success);

            var inWilds = Audit().Findings("area.wilds");
            Assert.Equal(new[] { "loot", "ogres" }, inWilds.Select(f => f.RuleId).ToArray());
            Assert.Equal(3, Audit().Findings().Count);
        }

        // ── Tolerance of unreadable files (spec review) ──────────────────────────

        [Fact]
        public async Task Malformed_area_or_mob_file_yields_no_finding_and_never_throws()
        {
            Area("area.wilds", B1);
            Area("area.broken", "designation:\n  tier: 0\n  bandMin: 3\n  bandMax: 1\n");
            Mob("mob.b3", 0, 3);
            Write("mobs", "mob.broken", "blueprintId: mob.broken\nname: broken\nlevel: [not, an, int]\n");
            Room("room.glade", "area.wilds", ("bad", "mob.broken"));
            Room("room.cave", "area.broken", ("ogres", "mob.b3"));

            var findings = Audit().Findings();
            Assert.Empty(findings);

            var result = await _catalog.UpsertSpawnRuleAsync("room.glade", new SpawnRule("bad-2", "mob.broken", 1, 60));
            Assert.True(result.Success);
            Assert.Empty(DesignationWarnings(result));
        }

        // ── Source parity (P6) ───────────────────────────────────────────────────

        [Fact]
        public async Task Registry_and_catalog_sources_produce_identical_findings()
        {
            Area("area.b1", B1);
            Area("area.b2", "designation:\n  tier: 0\n  bandMin: 2\n  bandMax: 3\n");
            Area("area.free");
            Mob("mob.b1", 0, 1);
            Mob("mob.b3", 0, 3);
            Mob("mob.unbanded", 0, 0);
            Item("item.b2", 0, 2);
            Room("room.a", "area.b1", ("ogres", "mob.b3"), ("rats", "mob.b1"), ("loot", "item.b2"));
            Room("room.b", "area.b2", ("rats", "mob.b1"), ("loot", "item.b2"), ("odd", "mob.unbanded"));
            Room("room.c", "area.free", ("ogres", "mob.b3"));
            Room("room.glade", "area.b1");

            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var loader = new WorldContentLoader(
                ecs, registry, Serializer(), new RoomContentWriter(Options), new WorldManifestStore(Options),
                new WorldConfiguration(), Options, NullLogger<WorldContentLoader>.Instance);
            await loader.LoadAndSpawnAsync();

            var fromRegistry = AreaPlacementCheck.Sweep(new RegistryPlacementSource(registry));
            var fromCatalog = Audit().Findings();

            Assert.Equal(3, fromRegistry.Count);
            Assert.Equal(fromRegistry, fromCatalog);
        }
    }
}
