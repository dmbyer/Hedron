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
using Hedron.Core.Modules.Stats;
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
                new StubAbilityRegistry(), new StubEffectRegistry(), new StubAspectRegistry(), ecs, registry);

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
            item.StatBonuses = new List<EquipmentStatBonus>
            {
                new(ScoreId.AttackPower, 7),
                new(ScoreId.Defense, 2),
            };
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
            Assert.Equal(
                new[] { new EquipmentStatBonus(ScoreId.AttackPower, 7), new EquipmentStatBonus(ScoreId.Defense, 2) },
                roundTripped.StatBonuses);
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

        // ── AreaBlueprintId resolution (WP1) ────────────────────────────────────────

        [Fact]
        public async Task List_Room_PopulatesAreaBlueprintId_OneHop()
        {
            var (catalog, _) = NewCatalog();

            // Create a room that references an area blueprint id directly.
            var roomDef = catalog.CreateNew(ContentKind.Room, "Entrance Hall");
            ((RoomTemplate)roomDef.Template).AreaId = "area.adhoc.target";
            await catalog.SaveAsync(roomDef);

            var list = catalog.List(ContentKind.Room);

            Assert.Single(list);
            Assert.Equal("area.adhoc.target", list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Room_BlankAreaId_YieldsNullAreaBlueprintId()
        {
            var (catalog, _) = NewCatalog();

            // Room with no AreaId set (defaults to empty string).
            var roomDef = catalog.CreateNew(ContentKind.Room, "Placeless Room");
            await catalog.SaveAsync(roomDef);

            var list = catalog.List(ContentKind.Room);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Item_PopulatesAreaBlueprintId_TwoHop()
        {
            var (catalog, _) = NewCatalog();

            // Room with an area.
            var roomDef = catalog.CreateNew(ContentKind.Room, "Armory");
            ((RoomTemplate)roomDef.Template).AreaId = "area.adhoc.castle";
            await catalog.SaveAsync(roomDef);

            // Item whose spawn room is that room.
            var itemDef = catalog.CreateNew(ContentKind.Item, "Longsword");
            ((ItemTemplate)itemDef.Template).SpawnRoomBlueprintId = roomDef.BlueprintId;
            await catalog.SaveAsync(itemDef);

            var list = catalog.List(ContentKind.Item);

            Assert.Single(list);
            Assert.Equal("area.adhoc.castle", list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Mob_PopulatesAreaBlueprintId_TwoHop()
        {
            var (catalog, _) = NewCatalog();

            // Room with an area.
            var roomDef = catalog.CreateNew(ContentKind.Room, "Guard Post");
            ((RoomTemplate)roomDef.Template).AreaId = "area.adhoc.fortress";
            await catalog.SaveAsync(roomDef);

            // Mob whose spawn room is that room.
            var mobDef = catalog.CreateNew(ContentKind.Mob, "Guard");
            ((MobTemplate)mobDef.Template).SpawnRoomBlueprintId = roomDef.BlueprintId;
            await catalog.SaveAsync(mobDef);

            var list = catalog.List(ContentKind.Mob);

            Assert.Single(list);
            Assert.Equal("area.adhoc.fortress", list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Item_BlankSpawnRoomBlueprintId_YieldsNull_DoesNotThrow()
        {
            var (catalog, _) = NewCatalog();

            // Item with no spawn room set.
            var itemDef = catalog.CreateNew(ContentKind.Item, "Floating Orb");
            // SpawnRoomBlueprintId defaults to string.Empty
            await catalog.SaveAsync(itemDef);

            var list = catalog.List(ContentKind.Item);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Mob_BlankSpawnRoomBlueprintId_YieldsNull_DoesNotThrow()
        {
            var (catalog, _) = NewCatalog();

            var mobDef = catalog.CreateNew(ContentKind.Mob, "Wandering Spirit");
            await catalog.SaveAsync(mobDef);

            var list = catalog.List(ContentKind.Mob);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Item_DanglingSpawnRoomBlueprintId_YieldsNull_DoesNotThrow()
        {
            var (catalog, _) = NewCatalog();

            // Item referencing a room that does not exist on disk.
            var itemDef = catalog.CreateNew(ContentKind.Item, "Lost Artifact");
            ((ItemTemplate)itemDef.Template).SpawnRoomBlueprintId = "room.adhoc.nonexistent";
            await catalog.SaveAsync(itemDef);

            // No rooms written — the spawn room is dangling.
            var list = catalog.List(ContentKind.Item);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Mob_DanglingSpawnRoomBlueprintId_YieldsNull_DoesNotThrow()
        {
            var (catalog, _) = NewCatalog();

            var mobDef = catalog.CreateNew(ContentKind.Mob, "Shadow Wraith");
            ((MobTemplate)mobDef.Template).SpawnRoomBlueprintId = "room.adhoc.ghost";
            await catalog.SaveAsync(mobDef);

            var list = catalog.List(ContentKind.Mob);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Item_SpawnRoomExistsButBlankAreaId_YieldsNull()
        {
            var (catalog, _) = NewCatalog();

            // Room exists but has no AreaId.
            var roomDef = catalog.CreateNew(ContentKind.Room, "Unassigned Room");
            // AreaId left blank (default)
            await catalog.SaveAsync(roomDef);

            var itemDef = catalog.CreateNew(ContentKind.Item, "Orphan Item");
            ((ItemTemplate)itemDef.Template).SpawnRoomBlueprintId = roomDef.BlueprintId;
            await catalog.SaveAsync(itemDef);

            var list = catalog.List(ContentKind.Item);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        [Fact]
        public async Task List_Area_AlwaysYieldsNullAreaBlueprintId()
        {
            var (catalog, _) = NewCatalog();

            var areaDef = catalog.CreateNew(ContentKind.Area, "The Wilds");
            await catalog.SaveAsync(areaDef);

            var list = catalog.List(ContentKind.Area);

            Assert.Single(list);
            Assert.Null(list[0].AreaBlueprintId);
        }

        // ── RoomsInArea query (WP1) ──────────────────────────────────────────────────

        [Fact]
        public async Task RoomsInArea_ReturnsOnlyMatchingRooms()
        {
            var (catalog, _) = NewCatalog();

            var r1 = catalog.CreateNew(ContentKind.Room, "Hall");
            ((RoomTemplate)r1.Template).AreaId = "area.adhoc.alpha";
            await catalog.SaveAsync(r1);

            var r2 = catalog.CreateNew(ContentKind.Room, "Cellar");
            ((RoomTemplate)r2.Template).AreaId = "area.adhoc.alpha";
            await catalog.SaveAsync(r2);

            var r3 = catalog.CreateNew(ContentKind.Room, "Rooftop");
            ((RoomTemplate)r3.Template).AreaId = "area.adhoc.beta";
            await catalog.SaveAsync(r3);

            var inAlpha = catalog.RoomsInArea("area.adhoc.alpha");

            Assert.Equal(2, inAlpha.Count);
            Assert.Contains(inAlpha, s => s.BlueprintId == r1.BlueprintId);
            Assert.Contains(inAlpha, s => s.BlueprintId == r2.BlueprintId);
            Assert.DoesNotContain(inAlpha, s => s.BlueprintId == r3.BlueprintId);
        }

        [Fact]
        public async Task RoomsInArea_ExcludesNonMatchingRooms()
        {
            var (catalog, _) = NewCatalog();

            var r1 = catalog.CreateNew(ContentKind.Room, "Tower");
            ((RoomTemplate)r1.Template).AreaId = "area.adhoc.beta";
            await catalog.SaveAsync(r1);

            var inAlpha = catalog.RoomsInArea("area.adhoc.alpha");

            Assert.Empty(inAlpha);
        }

        [Fact]
        public void RoomsInArea_UnknownAreaId_ReturnsEmpty()
        {
            var (catalog, _) = NewCatalog();

            // No rooms written — content dir is empty.
            var result = catalog.RoomsInArea("area.adhoc.unknown");

            Assert.Empty(result);
        }

        [Fact]
        public async Task RoomsInArea_UnknownAreaId_WithSomeRoomsOnDisk_ReturnsEmpty()
        {
            var (catalog, _) = NewCatalog();

            var r1 = catalog.CreateNew(ContentKind.Room, "Cave");
            ((RoomTemplate)r1.Template).AreaId = "area.adhoc.gamma";
            await catalog.SaveAsync(r1);

            var result = catalog.RoomsInArea("area.adhoc.unknown");

            Assert.Empty(result);
        }

        // ── WP2: Delete cascade-clear (one test per referrer type) ───────────────────

        [Fact]
        public async Task Delete_ClearAreaId_OnReferringRoom()
        {
            // Deleting room X clears AreaId on a room that references it.
            // (This tests the Room→Area path: delete area X, clear room.AreaId.)
            var (catalog, _) = NewCatalog();

            var area = catalog.CreateNew(ContentKind.Area, "The Area");
            await catalog.SaveAsync(area);

            var room = catalog.CreateNew(ContentKind.Room, "Foyer");
            ((RoomTemplate)room.Template).AreaId = area.BlueprintId;
            await catalog.SaveAsync(room);

            var result = await catalog.DeleteAsync(ContentKind.Area, area.BlueprintId);

            // Target file deleted.
            Assert.Null(catalog.Load(ContentKind.Area, area.BlueprintId));

            // Referring room's AreaId was cleared.
            var reloaded = (RoomTemplate)catalog.Load(ContentKind.Room, room.BlueprintId)!.Template;
            Assert.Empty(reloaded.AreaId);

            // Result enumerates the edit.
            Assert.Equal(area.BlueprintId, result.DeletedBlueprintId);
            Assert.Single(result.CascadeEdits);
            Assert.Equal(ContentKind.Room, result.CascadeEdits[0].ReferrerKind);
            Assert.Equal(room.BlueprintId, result.CascadeEdits[0].ReferrerBlueprintId);
            Assert.Equal("AreaId", result.CascadeEdits[0].FieldLabel);
        }

        [Fact]
        public async Task Delete_RemovesExitEntry_OnReferringRoom()
        {
            // Deleting room X removes the exit entry on a room that points to X.
            var (catalog, _) = NewCatalog();

            var targetRoom = catalog.CreateNew(ContentKind.Room, "East Room");
            await catalog.SaveAsync(targetRoom);

            var sourceRoom = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)sourceRoom.Template).Exits[Direction.East] = targetRoom.BlueprintId;
            await catalog.SaveAsync(sourceRoom);

            var result = await catalog.DeleteAsync(ContentKind.Room, targetRoom.BlueprintId);

            // Target file deleted.
            Assert.Null(catalog.Load(ContentKind.Room, targetRoom.BlueprintId));

            // Source room's East exit removed.
            var reloaded = (RoomTemplate)catalog.Load(ContentKind.Room, sourceRoom.BlueprintId)!.Template;
            Assert.False(reloaded.Exits.ContainsKey(Direction.East));

            // Result enumerates the cascade edit.
            Assert.Single(result.CascadeEdits);
            Assert.Equal("Exits[East]", result.CascadeEdits[0].FieldLabel);
        }

        [Fact]
        public async Task Delete_ClearsSpawnRoomBlueprintId_OnReferringItem()
        {
            // Deleting room X clears SpawnRoomBlueprintId on a referring item.
            var (catalog, _) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Armory");
            await catalog.SaveAsync(room);

            var item = catalog.CreateNew(ContentKind.Item, "Sword");
            ((ItemTemplate)item.Template).SpawnRoomBlueprintId = room.BlueprintId;
            await catalog.SaveAsync(item);

            var result = await catalog.DeleteAsync(ContentKind.Room, room.BlueprintId);

            // Target file deleted.
            Assert.Null(catalog.Load(ContentKind.Room, room.BlueprintId));

            // Item's spawn room cleared.
            var reloadedItem = (ItemTemplate)catalog.Load(ContentKind.Item, item.BlueprintId)!.Template;
            Assert.Empty(reloadedItem.SpawnRoomBlueprintId);

            // Result enumerates the cascade edit.
            Assert.Contains(result.CascadeEdits, e =>
                e.ReferrerKind == ContentKind.Item
                && e.ReferrerBlueprintId == item.BlueprintId
                && e.FieldLabel == "SpawnRoomBlueprintId");
        }

        [Fact]
        public async Task Delete_ClearsSpawnRoomBlueprintId_OnReferringMob()
        {
            // Deleting room X clears SpawnRoomBlueprintId on a referring mob.
            var (catalog, _) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Guard Post");
            await catalog.SaveAsync(room);

            var mob = catalog.CreateNew(ContentKind.Mob, "Guard");
            ((MobTemplate)mob.Template).SpawnRoomBlueprintId = room.BlueprintId;
            await catalog.SaveAsync(mob);

            var result = await catalog.DeleteAsync(ContentKind.Room, room.BlueprintId);

            // Target file deleted.
            Assert.Null(catalog.Load(ContentKind.Room, room.BlueprintId));

            // Mob's spawn room cleared.
            var reloadedMob = (MobTemplate)catalog.Load(ContentKind.Mob, mob.BlueprintId)!.Template;
            Assert.Empty(reloadedMob.SpawnRoomBlueprintId);

            // Result enumerates the cascade edit.
            Assert.Contains(result.CascadeEdits, e =>
                e.ReferrerKind == ContentKind.Mob
                && e.ReferrerBlueprintId == mob.BlueprintId
                && e.FieldLabel == "SpawnRoomBlueprintId");
        }

        [Fact]
        public async Task Delete_RemovesRoomFromAreaRoomsList()
        {
            // Deleting room X removes X from a referring area's Rooms list.
            var (catalog, _) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Cave Chamber");
            await catalog.SaveAsync(room);

            var area = catalog.CreateNew(ContentKind.Area, "The Cave");
            ((AreaTemplate)area.Template).Rooms.Add(room.BlueprintId);
            await catalog.SaveAsync(area);

            var result = await catalog.DeleteAsync(ContentKind.Room, room.BlueprintId);

            // Target file deleted.
            Assert.Null(catalog.Load(ContentKind.Room, room.BlueprintId));

            // Room removed from area's Rooms list.
            var reloadedArea = (AreaTemplate)catalog.Load(ContentKind.Area, area.BlueprintId)!.Template;
            Assert.DoesNotContain(room.BlueprintId, reloadedArea.Rooms);

            // Result enumerates the cascade edit.
            Assert.Contains(result.CascadeEdits, e =>
                e.ReferrerKind == ContentKind.Area
                && e.ReferrerBlueprintId == area.BlueprintId
                && e.FieldLabel == "Rooms[]");
        }

        [Fact]
        public async Task Delete_TouchesNoEntityService_NoSqlite()
        {
            // INV-22/23: The catalog ctor has no EntityService or persistence port.
            // Structural assertion: only file/writer ops occur — the ecs from NewCatalog()
            // has nothing added to it by the delete operation.
            var (catalog, ecs) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Delete-Test Room");
            await catalog.SaveAsync(room);

            var entityCountBefore = ecs.GetAllComponents<RoomComponent>().Count();

            await catalog.DeleteAsync(ContentKind.Room, room.BlueprintId);

            // EntityService is completely unchanged — delete is file-only.
            Assert.Equal(entityCountBefore, ecs.GetAllComponents<RoomComponent>().Count());

            // The catalog's ctor signature takes no EntityService or IPersistenceSystem —
            // verified by the fact that NewCatalog() constructs it without those deps
            // and all Delete operations above succeed without them.
        }

        // ── WP2: Warn-but-allow save ──────────────────────────────────────────────────

        [Fact]
        public async Task SaveAsync_WarnButAllow_OnDanglingAreaId_FileWritten()
        {
            // A structurally valid room with a non-resolving AreaId →
            // Success = true, Warnings non-empty, AND the YAML file is written.
            var (catalog, _) = NewCatalog();

            var roomDef = catalog.CreateNew(ContentKind.Room, "Orphan Room");
            ((RoomTemplate)roomDef.Template).AreaId = "area.adhoc.nonexistent";

            var result = await catalog.SaveAsync(roomDef);

            Assert.True(result.Success);
            Assert.NotEmpty(result.Warnings);
            Assert.Contains("area.adhoc.nonexistent", result.Warnings[0]);
            // File must be present on disk.
            Assert.NotNull(catalog.Load(ContentKind.Room, roomDef.BlueprintId));
        }

        [Fact]
        public async Task SaveAsync_StructuralFailure_StillBlocks_NoFileWritten()
        {
            // A structurally invalid definition → Failed, no file written (regression guard).
            var (catalog, _) = NewCatalog();

            var def = catalog.CreateNew(ContentKind.Area, "Bad Area");
            // Aspect weights sum to 60, not 100 — structurally invalid.
            ((AreaTemplate)def.Template).AspectAffinities =
                new Dictionary<AspectId, int> { [AspectId.Fire] = 60 };

            var result = await catalog.SaveAsync(def);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
            Assert.Null(catalog.Load(ContentKind.Area, def.BlueprintId));
        }

        [Fact]
        public async Task SaveAsync_NoWarnings_WhenAllRefsResolve()
        {
            // A valid room whose AreaId resolves → Success, empty Warnings.
            var (catalog, _) = NewCatalog();

            var area = catalog.CreateNew(ContentKind.Area, "Real Area");
            await catalog.SaveAsync(area);

            var room = catalog.CreateNew(ContentKind.Room, "Connected Room");
            ((RoomTemplate)room.Template).AreaId = area.BlueprintId;

            var result = await catalog.SaveAsync(room);

            Assert.True(result.Success);
            Assert.Empty(result.Warnings);
        }

        // ── WP2: Bidirectional room save ─────────────────────────────────────────────

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_WritesInverseExit()
        {
            // Saving room A east→B with bidirectional=true writes B's west→A.
            var (catalog, _) = NewCatalog();

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            await catalog.SaveAsync(roomB);

            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;

            var result = await catalog.SaveRoomAsync((RoomTemplate)roomA.Template, bidirectional: true);

            Assert.True(result.Success);
            Assert.Empty(result.Warnings);

            // B's west exit was written.
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.True(reloadedB.Exits.TryGetValue(Direction.West, out var westTarget));
            Assert.Equal(roomA.BlueprintId, westTarget);
        }

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_Conflict_WarnAndSkip()
        {
            // B already has west→C; saving A east→B bidirectional warns and does NOT overwrite B.
            var (catalog, _) = NewCatalog();

            var roomC = catalog.CreateNew(ContentKind.Room, "Third Room");
            await catalog.SaveAsync(roomC);

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            ((RoomTemplate)roomB.Template).Exits[Direction.West] = roomC.BlueprintId;
            await catalog.SaveAsync(roomB);

            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;

            var result = await catalog.SaveRoomAsync((RoomTemplate)roomA.Template, bidirectional: true);

            Assert.True(result.Success);
            Assert.NotEmpty(result.Warnings);
            Assert.Contains(roomB.BlueprintId, result.Warnings[0]);

            // B's west exit is unchanged (still points to C, not A).
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.Equal(roomC.BlueprintId, reloadedB.Exits[Direction.West]);
        }

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_AlreadyCorrect_SilentNoOp()
        {
            // B already has west→A; saving A east→B bidirectional produces no warning and no rewrite.
            var (catalog, _) = NewCatalog();

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            // We don't know roomA's blueprint id yet — create A first, then save B with the inverse.
            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;
            ((RoomTemplate)roomB.Template).Exits[Direction.West] = roomA.BlueprintId;

            await catalog.SaveAsync(roomB);
            await catalog.SaveAsync(roomA);  // Write A first so both exist on disk

            // Now save A again bidirectionally — B already has the correct inverse.
            var result = await catalog.SaveRoomAsync((RoomTemplate)roomA.Template, bidirectional: true);

            Assert.True(result.Success);
            Assert.Empty(result.Warnings);

            // B is unchanged.
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.Equal(roomA.BlueprintId, reloadedB.Exits[Direction.West]);
        }

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_SelfLoop_SilentNoOp()
        {
            // A room whose exit points at itself (self-loop) → no write, no warning.
            var (catalog, _) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Mirror Room");
            ((RoomTemplate)room.Template).Exits[Direction.North] = room.BlueprintId;
            await catalog.SaveAsync(room);

            var result = await catalog.SaveRoomAsync((RoomTemplate)room.Template, bidirectional: true);

            Assert.True(result.Success);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_False_DoesNotWriteInverse()
        {
            // Bidirectional=false behaves like SaveAsync — no inverse exit written.
            var (catalog, _) = NewCatalog();

            var roomB = catalog.CreateNew(ContentKind.Room, "Target Room");
            await catalog.SaveAsync(roomB);

            var roomA = catalog.CreateNew(ContentKind.Room, "Source Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.North] = roomB.BlueprintId;

            await catalog.SaveRoomAsync((RoomTemplate)roomA.Template, bidirectional: false);

            // B has no south exit.
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.False(reloadedB.Exits.ContainsKey(Direction.South));
        }

        // ── RemoveRoomExitAsync policy matrix (world-editor-grid Postcondition 6) ─────

        [Fact]
        public async Task RemoveRoomExitAsync_RemovesSourceExit_AndWritesSource()
        {
            var (catalog, _) = NewCatalog();

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            await catalog.SaveAsync(roomB);

            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;
            await catalog.SaveAsync(roomA);

            var result = await catalog.RemoveRoomExitAsync(roomA.BlueprintId, Direction.East, bidirectional: false);

            Assert.True(result.Success);
            var reloadedA = (RoomTemplate)catalog.Load(ContentKind.Room, roomA.BlueprintId)!.Template;
            Assert.False(reloadedA.Exits.ContainsKey(Direction.East));
        }

        [Fact]
        public async Task RemoveRoomExitAsync_Bidirectional_RemovesReciprocal_WhenItPointsBack()
        {
            var (catalog, _) = NewCatalog();

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;
            ((RoomTemplate)roomB.Template).Exits[Direction.West] = roomA.BlueprintId;
            await catalog.SaveAsync(roomB);
            await catalog.SaveAsync(roomA);

            var result = await catalog.RemoveRoomExitAsync(roomA.BlueprintId, Direction.East, bidirectional: true);

            Assert.True(result.Success);
            var reloadedA = (RoomTemplate)catalog.Load(ContentKind.Room, roomA.BlueprintId)!.Template;
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.False(reloadedA.Exits.ContainsKey(Direction.East));
            Assert.False(reloadedB.Exits.ContainsKey(Direction.West));
        }

        [Fact]
        public async Task RemoveRoomExitAsync_Bidirectional_LeavesForeignInverseUntouched()
        {
            // B's west exit points at C, not A — removing A's east exit must not touch B's west exit.
            var (catalog, _) = NewCatalog();

            var roomC = catalog.CreateNew(ContentKind.Room, "Third Room");
            await catalog.SaveAsync(roomC);

            var roomB = catalog.CreateNew(ContentKind.Room, "East Room");
            ((RoomTemplate)roomB.Template).Exits[Direction.West] = roomC.BlueprintId;
            await catalog.SaveAsync(roomB);

            var roomA = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)roomA.Template).Exits[Direction.East] = roomB.BlueprintId;
            await catalog.SaveAsync(roomA);

            var result = await catalog.RemoveRoomExitAsync(roomA.BlueprintId, Direction.East, bidirectional: true);

            Assert.True(result.Success);
            var reloadedA = (RoomTemplate)catalog.Load(ContentKind.Room, roomA.BlueprintId)!.Template;
            var reloadedB = (RoomTemplate)catalog.Load(ContentKind.Room, roomB.BlueprintId)!.Template;
            Assert.False(reloadedA.Exits.ContainsKey(Direction.East));
            Assert.Equal(roomC.BlueprintId, reloadedB.Exits[Direction.West]);
        }

        [Fact]
        public async Task RemoveRoomExitAsync_AbsentExit_IsNoOpSuccess()
        {
            var (catalog, _) = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "Empty Room");
            await catalog.SaveAsync(room);

            var result = await catalog.RemoveRoomExitAsync(room.BlueprintId, Direction.North, bidirectional: true);

            Assert.True(result.Success);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public async Task RemoveRoomExitAsync_UnknownRoom_Fails()
        {
            var (catalog, _) = NewCatalog();

            var result = await catalog.RemoveRoomExitAsync("room.nonexistent", Direction.North, bidirectional: false);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }
    }
}
