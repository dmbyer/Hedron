using System;
using System.Collections.Generic;
using System.IO;
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
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
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
    /// System-unit tests for <see cref="ContentDefinitionCatalog"/> — the shared content-definition
    /// layer both content-tooling tracks consume. Each test points the catalog at a fresh temp
    /// content directory with the real writers, serializer, and validator (no mocks).
    /// </summary>
    public sealed class ContentDefinitionCatalogTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        // ── Empty registry stubs for the validator (no cross-refs to satisfy here) ──

        private sealed class StubAbilityRegistry
            : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
        {
            public StubAbilityRegistry() : base(Array.Empty<AbilityDefinition>(), d => d.Id) { }
        }

        private sealed class StubEffectRegistry
            : DefinitionRegistry<string, EffectDefinition>, IEffectRegistry
        {
            public StubEffectRegistry() : base(Array.Empty<EffectDefinition>(), d => d.EffectId) { }
        }

        private sealed class StubAspectRegistry
            : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
        {
            public StubAspectRegistry() : base(Array.Empty<AspectDefinition>(), d => d.Id) { }
        }

        private (ContentDefinitionCatalog catalog, EntityService ecs) NewCatalog()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);

            var options = Options.Create(new WorldOptions { ContentDirectory = dir });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });

            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var validator = new ContentValidator(
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs);

            var catalog = new ContentDefinitionCatalog(
                serializer,
                validator,
                registry,
                new AreaContentWriter(options),
                new RoomContentWriter(options),
                new ItemContentWriter(options),
                new MobContentWriter(options),
                options,
                NullLogger<ContentDefinitionCatalog>.Instance);

            return (catalog, ecs);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        // ── Tests ───────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Load_DeserializesExistingDefinition()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Area, "Test Area");
            ((AreaTemplate)def.Template).Description = "A test area.";
            await catalog.SaveAsync(def);

            var loaded = catalog.Load(ContentKind.Area, def.BlueprintId);

            Assert.NotNull(loaded);
            var area = Assert.IsType<AreaTemplate>(loaded!.Template);
            Assert.Equal("Test Area", area.Name);
            Assert.Equal("A test area.", area.Description);
        }

        [Fact]
        public async Task SaveAsync_WritesValidDefinition_RoundTrips_Area()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Area, "Round Area");
            var result = await catalog.SaveAsync(def);
            Assert.True(result.Success);

            var loaded = catalog.Load(ContentKind.Area, def.BlueprintId);
            Assert.Equal(def.BlueprintId, loaded!.BlueprintId);
            Assert.Equal("Round Area", ((AreaTemplate)loaded.Template).Name);
        }

        [Fact]
        public async Task SaveAsync_WritesValidDefinition_RoundTrips_Room()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Room, "Round Room");
            var result = await catalog.SaveAsync(def);
            Assert.True(result.Success);

            var loaded = catalog.Load(ContentKind.Room, def.BlueprintId);
            Assert.Equal("Round Room", ((RoomTemplate)loaded!.Template).Name);
        }

        [Fact]
        public async Task SaveAsync_WritesValidDefinition_RoundTrips_Item()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Item, "Round Sword");
            var item = (ItemTemplate)def.Template;
            item.Description = "A keen blade.";
            item.Keywords = new List<string> { "sword", "blade" };
            item.ItemType = ItemType.Weapon;
            item.WornSlots = new List<WornSlot> { WornSlot.MainHand };
            item.DamageBonus = 7;
            item.SpawnRoomBlueprintId = "room.adhoc.abc";

            var result = await catalog.SaveAsync(def);
            Assert.True(result.Success);

            var loaded = catalog.Load(ContentKind.Item, def.BlueprintId);
            var roundTripped = Assert.IsType<ItemTemplate>(loaded!.Template);
            Assert.Equal("Round Sword", roundTripped.Name);
            Assert.Equal("A keen blade.", roundTripped.Description);
            Assert.Equal(new[] { "sword", "blade" }, roundTripped.Keywords);
            Assert.Equal(ItemType.Weapon, roundTripped.ItemType);
            Assert.Equal(new[] { WornSlot.MainHand }, roundTripped.WornSlots);
            Assert.Equal(7, roundTripped.DamageBonus);
            Assert.Equal("room.adhoc.abc", roundTripped.SpawnRoomBlueprintId);
        }

        [Fact]
        public async Task SaveAsync_WritesValidDefinition_RoundTrips_Mob()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Mob, "Round Goblin");
            var mob = (MobTemplate)def.Template;
            mob.Description = "A snarling goblin.";
            mob.Keywords = new List<string> { "goblin", "snarl" };
            mob.MobType = MobType.Creature;
            mob.SpawnRoomBlueprintId = "room.adhoc.def";
            mob.Level = 5;
            mob.MaxHp = 120;
            mob.Mind = 11;
            mob.Body = 12;
            mob.Spirit = 13;
            mob.Attunement = 14;
            mob.MaxMana = 60;
            mob.MaxStamina = 70;
            mob.MaxAstra = 15;

            var result = await catalog.SaveAsync(def);
            Assert.True(result.Success);

            var loaded = catalog.Load(ContentKind.Mob, def.BlueprintId);
            var roundTripped = Assert.IsType<MobTemplate>(loaded!.Template);
            Assert.Equal("Round Goblin", roundTripped.Name);
            Assert.Equal("A snarling goblin.", roundTripped.Description);
            Assert.Equal(new[] { "goblin", "snarl" }, roundTripped.Keywords);
            Assert.Equal(MobType.Creature, roundTripped.MobType);
            Assert.Equal("room.adhoc.def", roundTripped.SpawnRoomBlueprintId);
            Assert.Equal(5, roundTripped.Level);
            Assert.Equal(120, roundTripped.MaxHp);
            Assert.Equal(11, roundTripped.Mind);
            Assert.Equal(12, roundTripped.Body);
            Assert.Equal(13, roundTripped.Spirit);
            Assert.Equal(14, roundTripped.Attunement);
            Assert.Equal(60, roundTripped.MaxMana);
            Assert.Equal(70, roundTripped.MaxStamina);
            Assert.Equal(15, roundTripped.MaxAstra);
        }

        [Fact]
        public async Task SaveAsync_RejectsInvalidDefinition_AndWritesNoFile()
        {
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Area, "Bad Area");
            // Aspect weights sum to 60, not 100 — invalid composition.
            ((AreaTemplate)def.Template).AspectAffinities =
                new Dictionary<AspectId, int> { [AspectId.Fire] = 60 };

            var result = await catalog.SaveAsync(def);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
            // Validation-before-write: no file landed, so it cannot be loaded back.
            Assert.Null(catalog.Load(ContentKind.Area, def.BlueprintId));
        }

        [Fact]
        public async Task List_EnumeratesAllDefinitionsOfKind()
        {
            var (catalog, _) = NewCatalog();

            var a1 = catalog.CreateNew(ContentKind.Area, "Alpha");
            var a2 = catalog.CreateNew(ContentKind.Area, "Beta");
            await catalog.SaveAsync(a1);
            await catalog.SaveAsync(a2);

            var list = catalog.List(ContentKind.Area);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, s => s.Name == "Alpha");
            Assert.Contains(list, s => s.BlueprintId == a2.BlueprintId);
        }

        [Fact]
        public void CreateNew_GeneratesUniqueBlueprintId_AndCreatesNoLiveEntity()
        {
            var (catalog, ecs) = NewCatalog();

            var a = catalog.CreateNew(ContentKind.Area, "One");
            var b = catalog.CreateNew(ContentKind.Area, "Two");

            Assert.NotEqual(a.BlueprintId, b.BlueprintId);
            Assert.StartsWith("area.adhoc.", a.BlueprintId);
            // INV-12: CreateNew never touches the live world.
            Assert.Empty(ecs.GetAllComponents<AreaComponent>());
        }
    }
}
