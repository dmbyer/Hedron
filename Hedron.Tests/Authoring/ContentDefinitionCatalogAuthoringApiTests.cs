using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Hedron.Core.Modules.Stats;
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
    /// System-unit tests for the two pure authoring-API additions on
    /// <see cref="IContentDefinitionCatalog"/>: <c>WithBlueprintId</c> (the id-only rekey behind the
    /// editors' blueprint-id field) and <c>CreateNextFrom</c> (the per-kind carry-forward policy for
    /// "save and create next"). Both write nothing.
    /// </summary>
    public sealed class ContentDefinitionCatalogAuthoringApiTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

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

        private ContentDefinitionCatalog NewCatalog()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-catalog-api-" + Guid.NewGuid().ToString("N"));
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
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs, registry);

            return new ContentDefinitionCatalog(
                serializer, validator, registry,
                new ContentReferenceIndex(serializer, options, NullLogger<ContentReferenceIndex>.Instance),
                new AreaContentWriter(options), new RoomContentWriter(options),
                new ItemContentWriter(options), new MobContentWriter(options),
                options, NullLogger<ContentDefinitionCatalog>.Instance);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        // ── WithBlueprintId ─────────────────────────────────────────────────────────

        [Fact]
        public void WithBlueprintId_Area_PreservesEveryNonIdField()
        {
            var catalog = NewCatalog();
            var area = (AreaTemplate)catalog.CreateNew(ContentKind.Area, "Alpha").Template;
            area.Description = "A place.";
            area.AreaId = "legacy-area-key";
            area.RespawnRate = 42;
            area.Pvp = true;
            area.AspectAffinities = new Dictionary<AspectId, int> { [AspectId.Fire] = 3 };
            area.Rooms.Add("room.one");

            var result = (AreaTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Area, area), "area.deliberate").Template;

            Assert.Equal("area.deliberate", result.BlueprintId);
            Assert.Equal("Alpha", result.Name);
            Assert.Equal("A place.", result.Description);
            Assert.Equal("legacy-area-key", result.AreaId);
            Assert.Equal(42, result.RespawnRate);
            Assert.True(result.Pvp);
            Assert.Equal(3, result.AspectAffinities![AspectId.Fire]);
            Assert.Equal(new[] { "room.one" }, result.Rooms);
        }

        [Fact]
        public void WithBlueprintId_Room_PreservesEveryNonIdField()
        {
            var catalog = NewCatalog();
            var room = (RoomTemplate)catalog.CreateNew(ContentKind.Room, "Hall").Template;
            room.Description = "A hall.";
            room.AreaId = "area.alpha";
            room.X = 1; room.Y = -2; room.Z = 3;
            room.Exits[Direction.North] = "room.other";

            var result = (RoomTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Room, room), "room.deliberate").Template;

            Assert.Equal("room.deliberate", result.BlueprintId);
            Assert.Equal("Hall", result.Name);
            Assert.Equal("A hall.", result.Description);
            Assert.Equal("area.alpha", result.AreaId);
            Assert.Equal((1, -2, 3), (result.X, result.Y, result.Z));
            Assert.Equal("room.other", result.Exits[Direction.North]);
        }

        [Fact]
        public void WithBlueprintId_Item_PreservesEveryNonIdField()
        {
            var catalog = NewCatalog();
            var item = (ItemTemplate)catalog.CreateNew(ContentKind.Item, "Sword").Template;
            item.Description = "A keen blade.";
            item.Keywords = new List<string> { "sword", "blade" };
            item.ItemType = ItemType.Weapon;
            item.WornSlots = new List<WornSlot> { WornSlot.MainHand };
            item.SpawnRoomBlueprintId = "room.one";
            item.StatBonuses = new List<EquipmentStatBonus> { new(ScoreId.AttackPower, 7) };
            item.Value = 250;
            item.Tier = 3;
            item.Band = 2;

            var result = (ItemTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Item, item), "item.deliberate").Template;

            Assert.Equal("item.deliberate", result.BlueprintId);
            Assert.Equal("Sword", result.Name);
            Assert.Equal("A keen blade.", result.Description);
            Assert.Equal(new[] { "sword", "blade" }, result.Keywords);
            Assert.Equal(ItemType.Weapon, result.ItemType);
            Assert.Equal(new[] { WornSlot.MainHand }, result.WornSlots);
            Assert.Equal("room.one", result.SpawnRoomBlueprintId);
            Assert.Equal(new[] { new EquipmentStatBonus(ScoreId.AttackPower, 7) }, result.StatBonuses);
            Assert.Equal(250, result.Value);
            Assert.Equal(3, result.Tier);
            Assert.Equal(2, result.Band);
        }

        [Fact]
        public void WithBlueprintId_Mob_PreservesEveryNonIdField()
        {
            var catalog = NewCatalog();
            var mob = (MobTemplate)catalog.CreateNew(ContentKind.Mob, "Rat").Template;
            mob.Description = "A rat.";
            mob.Keywords = new List<string> { "rat" };
            mob.SpawnRoomBlueprintId = "room.one";
            mob.Level = 4;
            mob.MaxHp = 55;
            mob.Mind = 3; mob.Body = 6; mob.Spirit = 2;
            mob.MaxMana = 10; mob.MaxStamina = 11; mob.MaxAstra = 12;
            mob.Tier = 2;
            mob.Band = 1;
            mob.IsShop = true;
            mob.ShopTillSeed = 900;

            var result = (MobTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Mob, mob), "mob.deliberate").Template;

            Assert.Equal("mob.deliberate", result.BlueprintId);
            Assert.Equal("Rat", result.Name);
            Assert.Equal("A rat.", result.Description);
            Assert.Equal(new[] { "rat" }, result.Keywords);
            Assert.Equal("room.one", result.SpawnRoomBlueprintId);
            Assert.Equal(4, result.Level);
            Assert.Equal(55, result.MaxHp);
            Assert.Equal((3, 6, 2), (result.Mind, result.Body, result.Spirit));
            Assert.Equal((10, 11, 12), (result.MaxMana, result.MaxStamina, result.MaxAstra));
            Assert.Equal(2, result.Tier);
            Assert.Equal(1, result.Band);
            Assert.True(result.IsShop);
            Assert.Equal(900, result.ShopTillSeed);
        }

        [Fact]
        public void WithBlueprintId_BlankId_MintsAnAdhocId_AndStillPreservesFields()
        {
            var catalog = NewCatalog();
            var item = (ItemTemplate)catalog.CreateNew(ContentKind.Item, "Sword").Template;
            item.Value = 99;

            var result = (ItemTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Item, item), string.Empty).Template;

            Assert.StartsWith(ContentKind.Item.AdhocPrefix(), result.BlueprintId);
            Assert.Equal("Sword", result.Name);
            Assert.Equal(99, result.Value);
        }

        [Fact]
        public void WithBlueprintId_RewritesSelfReferentialIds_ConsistentlyWithRename()
        {
            var catalog = NewCatalog();
            var room = (RoomTemplate)catalog.CreateNew(ContentKind.Room, "Loop", "room.old").Template;
            room.Exits[Direction.North] = "room.old";   // self-loop
            room.Exits[Direction.South] = "room.other"; // external

            var result = (RoomTemplate)catalog
                .WithBlueprintId(new ContentDefinition(ContentKind.Room, room), "room.new").Template;

            Assert.Equal("room.new", result.Exits[Direction.North]);
            Assert.Equal("room.other", result.Exits[Direction.South]);
        }

        // ── CreateNextFrom ──────────────────────────────────────────────────────────

        [Fact]
        public void CreateNextFrom_Area_CarriesNothingForward()
        {
            var catalog = NewCatalog();
            var area = (AreaTemplate)catalog.CreateNew(ContentKind.Area, "Alpha").Template;
            area.Description = "A place.";
            area.RespawnRate = 42;
            area.Pvp = true;

            var next = (AreaTemplate)catalog
                .CreateNextFrom(new ContentDefinition(ContentKind.Area, area), "New Area").Template;

            Assert.NotEqual(area.BlueprintId, next.BlueprintId);
            Assert.Equal("New Area", next.Name);
            Assert.Equal(string.Empty, next.Description);
            Assert.Equal(new AreaTemplate("probe").RespawnRate, next.RespawnRate);
            Assert.False(next.Pvp);
        }

        [Fact]
        public void CreateNextFrom_Room_CarriesAreaId_AndResetsTheRest()
        {
            var catalog = NewCatalog();
            var room = (RoomTemplate)catalog.CreateNew(ContentKind.Room, "Hall").Template;
            room.AreaId = "area.alpha";
            room.Description = "A hall.";
            room.X = 5; room.Y = 6; room.Z = 7;
            room.Exits[Direction.North] = "room.other";

            var next = (RoomTemplate)catalog
                .CreateNextFrom(new ContentDefinition(ContentKind.Room, room), "New Room").Template;

            Assert.NotEqual(room.BlueprintId, next.BlueprintId);
            Assert.Equal("area.alpha", next.AreaId);
            Assert.Equal("New Room", next.Name);
            Assert.Equal(string.Empty, next.Description);
            Assert.Empty(next.Exits);
            // Coordinates reset to unauthored, not to the origin — the grid editor places the room.
            Assert.Null(next.X);
            Assert.Null(next.Y);
            Assert.Null(next.Z);
        }

        [Fact]
        public void CreateNextFrom_Item_CarriesTierBandTypeAndSlots_AndResetsTheRest()
        {
            var catalog = NewCatalog();
            var item = (ItemTemplate)catalog.CreateNew(ContentKind.Item, "Sword").Template;
            item.Tier = 3;
            item.Band = 2;
            item.ItemType = ItemType.Weapon;
            item.WornSlots = new List<WornSlot> { WornSlot.MainHand };
            item.Description = "A keen blade.";
            item.StatBonuses = new List<EquipmentStatBonus> { new(ScoreId.AttackPower, 7) };
            item.Value = 250;

            var next = (ItemTemplate)catalog
                .CreateNextFrom(new ContentDefinition(ContentKind.Item, item), "New Item").Template;

            Assert.NotEqual(item.BlueprintId, next.BlueprintId);
            Assert.Equal(3, next.Tier);
            Assert.Equal(2, next.Band);
            Assert.Equal(ItemType.Weapon, next.ItemType);
            Assert.Equal(new[] { WornSlot.MainHand }, next.WornSlots);
            Assert.Equal("New Item", next.Name);
            Assert.Equal(string.Empty, next.Description);
            Assert.Empty(next.StatBonuses);
            Assert.Equal(0, next.Value);

            // The carried slot list is a copy — editing the next definition must not touch the previous.
            next.WornSlots.Add(WornSlot.OffHand);
            Assert.Equal(new[] { WornSlot.MainHand }, item.WornSlots);
        }

        [Fact]
        public void CreateNextFrom_Mob_CarriesTierBandAndSpawnRoom_AndResetsTheRest()
        {
            var catalog = NewCatalog();
            var mob = (MobTemplate)catalog.CreateNew(ContentKind.Mob, "Rat").Template;
            mob.Tier = 2;
            mob.Band = 1;
            mob.SpawnRoomBlueprintId = "room.one";
            mob.Description = "A rat.";
            mob.Level = 4;
            mob.MaxHp = 55;
            mob.Body = 9;
            mob.MaxMana = 30;
            mob.IsShop = true;
            mob.ShopTillSeed = 900;

            var next = (MobTemplate)catalog
                .CreateNextFrom(new ContentDefinition(ContentKind.Mob, mob), "New Mob").Template;

            var fresh = new MobTemplate("probe");

            Assert.NotEqual(mob.BlueprintId, next.BlueprintId);
            Assert.Equal(2, next.Tier);
            Assert.Equal(1, next.Band);
            Assert.Equal("room.one", next.SpawnRoomBlueprintId);
            Assert.Equal("New Mob", next.Name);
            Assert.Equal(string.Empty, next.Description);
            Assert.Equal(fresh.Level, next.Level);
            Assert.Equal(fresh.MaxHp, next.MaxHp);
            Assert.Equal(fresh.Body, next.Body);
            Assert.Equal(fresh.MaxMana, next.MaxMana);
            Assert.False(next.IsShop);
            Assert.Equal(fresh.ShopTillSeed, next.ShopTillSeed);
            Assert.Empty(next.CurrencyLoot);
        }

        [Fact]
        public void CreateNextFrom_MintsDistinctIds_AcrossRepeatedCalls()
        {
            var catalog = NewCatalog();
            var previous = catalog.CreateNew(ContentKind.Item, "Sword");

            var ids = Enumerable.Range(0, 5)
                .Select(_ => catalog.CreateNextFrom(previous, "New Item").BlueprintId)
                .ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }
}
