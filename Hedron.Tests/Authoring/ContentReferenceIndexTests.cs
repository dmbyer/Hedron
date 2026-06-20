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
    /// System-unit tests for <see cref="ContentReferenceIndex"/> and
    /// <see cref="DirectionExtensions.Opposite"/>.
    ///
    /// Each fixture uses a fresh temp content directory with the real writers and serializer
    /// (no mocks) — the same fixture pattern as <see cref="ContentDefinitionCatalogTests"/>.
    /// </summary>
    public sealed class ContentReferenceIndexTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        // ── Stub registries (mirrors ContentDefinitionCatalogTests) ─────────────────

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

        // ── Fixture factory ──────────────────────────────────────────────────────────

        private (ContentReferenceIndex index, ContentDefinitionCatalog catalog) NewFixture()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-refidx-" + Guid.NewGuid().ToString("N"));
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

            var index = new ContentReferenceIndex(
                serializer,
                options,
                NullLogger<ContentReferenceIndex>.Instance);

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

            return (index, catalog);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        // ── Direction.Opposite — total and correct over all six members ──────────────

        [Theory]
        [InlineData(Direction.North, Direction.South)]
        [InlineData(Direction.South, Direction.North)]
        [InlineData(Direction.East,  Direction.West)]
        [InlineData(Direction.West,  Direction.East)]
        [InlineData(Direction.Up,    Direction.Down)]
        [InlineData(Direction.Down,  Direction.Up)]
        public void DirectionOpposite_IsCorrectAndTotal(Direction input, Direction expected)
        {
            Assert.Equal(expected, input.Opposite());
        }

        [Theory]
        [InlineData(Direction.North)]
        [InlineData(Direction.South)]
        [InlineData(Direction.East)]
        [InlineData(Direction.West)]
        [InlineData(Direction.Up)]
        [InlineData(Direction.Down)]
        public void DirectionOpposite_IsItsOwnInverse(Direction d)
        {
            // Opposite is an involution: d.Opposite().Opposite() == d
            Assert.Equal(d, d.Opposite().Opposite());
        }

        // ── Resolves — present and absent targets ────────────────────────────────────

        [Fact]
        public async Task Resolves_ReturnsTrue_ForPresentArea()
        {
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "Test Area");
            await catalog.SaveAsync(area);

            Assert.True(index.Resolves(ContentKind.Area, area.BlueprintId));
        }

        [Fact]
        public void Resolves_ReturnsFalse_ForAbsentArea()
        {
            var (index, _) = NewFixture();

            Assert.False(index.Resolves(ContentKind.Area, "area.adhoc.nonexistent"));
        }

        [Fact]
        public async Task Resolves_ReturnsTrue_ForPresentRoom()
        {
            var (index, catalog) = NewFixture();

            var room = catalog.CreateNew(ContentKind.Room, "Test Room");
            await catalog.SaveAsync(room);

            Assert.True(index.Resolves(ContentKind.Room, room.BlueprintId));
        }

        [Fact]
        public void Resolves_ReturnsFalse_ForAbsentRoom()
        {
            var (index, _) = NewFixture();

            Assert.False(index.Resolves(ContentKind.Room, "room.adhoc.nonexistent"));
        }

        // ── Referrers — each declared edge type exercised ────────────────────────────

        [Fact]
        public async Task Referrers_MatchesRoom_ViaAreaId()
        {
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "The Area");
            await catalog.SaveAsync(area);

            var room = catalog.CreateNew(ContentKind.Room, "Foyer");
            ((RoomTemplate)room.Template).AreaId = area.BlueprintId;
            await catalog.SaveAsync(room);

            var referrers = index.Referrers(ContentKind.Area, area.BlueprintId);

            Assert.Single(referrers);
            Assert.Equal(ContentKind.Room, referrers[0].ReferrerKind);
            Assert.Equal(room.BlueprintId, referrers[0].ReferrerBlueprintId);
            Assert.Equal("AreaId", referrers[0].FieldLabel);
        }

        [Fact]
        public async Task Referrers_DoesNotMatch_Room_AsAreaTarget_WhenSearchingAreaKind()
        {
            // A room with AreaId → area.X should NOT appear as a room-kind referrer of area.X
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "The Area");
            await catalog.SaveAsync(area);

            var room = catalog.CreateNew(ContentKind.Room, "Foyer");
            ((RoomTemplate)room.Template).AreaId = area.BlueprintId;
            await catalog.SaveAsync(room);

            // Searching for Room-kind referrers of the area id should yield nothing
            // (rooms reference areas via AreaId; they are not referenced AS rooms in this context)
            var referrers = index.Referrers(ContentKind.Room, area.BlueprintId);

            Assert.Empty(referrers);
        }

        [Fact]
        public async Task Referrers_MatchesRoom_ViaExitDirection()
        {
            var (index, catalog) = NewFixture();

            var targetRoom = catalog.CreateNew(ContentKind.Room, "East Room");
            await catalog.SaveAsync(targetRoom);

            var sourceRoom = catalog.CreateNew(ContentKind.Room, "West Room");
            ((RoomTemplate)sourceRoom.Template).Exits[Direction.East] = targetRoom.BlueprintId;
            await catalog.SaveAsync(sourceRoom);

            var referrers = index.Referrers(ContentKind.Room, targetRoom.BlueprintId);

            Assert.Single(referrers);
            Assert.Equal(ContentKind.Room, referrers[0].ReferrerKind);
            Assert.Equal(sourceRoom.BlueprintId, referrers[0].ReferrerBlueprintId);
            Assert.Equal("Exits[East]", referrers[0].FieldLabel);
        }

        [Fact]
        public async Task Referrers_MatchesItem_ViaSpawnRoomBlueprintId()
        {
            var (index, catalog) = NewFixture();

            var room = catalog.CreateNew(ContentKind.Room, "Armory");
            await catalog.SaveAsync(room);

            var item = catalog.CreateNew(ContentKind.Item, "Sword");
            ((ItemTemplate)item.Template).SpawnRoomBlueprintId = room.BlueprintId;
            await catalog.SaveAsync(item);

            var referrers = index.Referrers(ContentKind.Room, room.BlueprintId);

            Assert.Contains(referrers, r =>
                r.ReferrerKind == ContentKind.Item
                && r.ReferrerBlueprintId == item.BlueprintId
                && r.FieldLabel == "SpawnRoomBlueprintId");
        }

        [Fact]
        public async Task Referrers_MatchesMob_ViaSpawnRoomBlueprintId()
        {
            var (index, catalog) = NewFixture();

            var room = catalog.CreateNew(ContentKind.Room, "Guard Post");
            await catalog.SaveAsync(room);

            var mob = catalog.CreateNew(ContentKind.Mob, "Guard");
            ((MobTemplate)mob.Template).SpawnRoomBlueprintId = room.BlueprintId;
            await catalog.SaveAsync(mob);

            var referrers = index.Referrers(ContentKind.Room, room.BlueprintId);

            Assert.Contains(referrers, r =>
                r.ReferrerKind == ContentKind.Mob
                && r.ReferrerBlueprintId == mob.BlueprintId
                && r.FieldLabel == "SpawnRoomBlueprintId");
        }

        // ── SweepBroken — enumerates exactly the dangling edges ──────────────────────

        [Fact]
        public async Task SweepBroken_EnumeratesExactlyDanglingEdges_MixedFixture()
        {
            var (index, catalog) = NewFixture();

            // Room with a dangling AreaId → no area file on disk
            var roomBrokenArea = catalog.CreateNew(ContentKind.Room, "Orphan Room");
            ((RoomTemplate)roomBrokenArea.Template).AreaId = "area.adhoc.missing";
            await catalog.SaveAsync(roomBrokenArea);

            // Room with a dangling exit → no target room file on disk
            var roomBrokenExit = catalog.CreateNew(ContentKind.Room, "Dead End");
            ((RoomTemplate)roomBrokenExit.Template).Exits[Direction.North] = "room.adhoc.nowhere";
            await catalog.SaveAsync(roomBrokenExit);

            // Item with a dangling SpawnRoomBlueprintId → no room file on disk
            var itemBroken = catalog.CreateNew(ContentKind.Item, "Lost Sword");
            ((ItemTemplate)itemBroken.Template).SpawnRoomBlueprintId = "room.adhoc.ghost";
            await catalog.SaveAsync(itemBroken);

            // Mob with a dangling SpawnRoomBlueprintId → no room file on disk
            var mobBroken = catalog.CreateNew(ContentKind.Mob, "Shadow Wraith");
            ((MobTemplate)mobBroken.Template).SpawnRoomBlueprintId = "room.adhoc.phantom";
            await catalog.SaveAsync(mobBroken);

            var broken = index.SweepBroken();

            // Exactly four broken references — one per declared edge kind.
            Assert.Equal(4, broken.Count);

            Assert.Contains(broken, b =>
                b.SourceKind == ContentKind.Room
                && b.SourceBlueprintId == roomBrokenArea.BlueprintId
                && b.FieldLabel == "AreaId"
                && b.MissingTargetId == "area.adhoc.missing");

            Assert.Contains(broken, b =>
                b.SourceKind == ContentKind.Room
                && b.SourceBlueprintId == roomBrokenExit.BlueprintId
                && b.FieldLabel == "Exits[North]"
                && b.MissingTargetId == "room.adhoc.nowhere");

            Assert.Contains(broken, b =>
                b.SourceKind == ContentKind.Item
                && b.SourceBlueprintId == itemBroken.BlueprintId
                && b.FieldLabel == "SpawnRoomBlueprintId"
                && b.MissingTargetId == "room.adhoc.ghost");

            Assert.Contains(broken, b =>
                b.SourceKind == ContentKind.Mob
                && b.SourceBlueprintId == mobBroken.BlueprintId
                && b.FieldLabel == "SpawnRoomBlueprintId"
                && b.MissingTargetId == "room.adhoc.phantom");
        }

        [Fact]
        public async Task SweepBroken_ReturnsEmpty_WhenAllEdgesResolve()
        {
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "Home");
            await catalog.SaveAsync(area);

            var roomA = catalog.CreateNew(ContentKind.Room, "Entrance");
            ((RoomTemplate)roomA.Template).AreaId = area.BlueprintId;
            await catalog.SaveAsync(roomA);

            var roomB = catalog.CreateNew(ContentKind.Room, "Corridor");
            ((RoomTemplate)roomB.Template).AreaId = area.BlueprintId;
            ((RoomTemplate)roomB.Template).Exits[Direction.South] = roomA.BlueprintId;
            await catalog.SaveAsync(roomB);

            var item = catalog.CreateNew(ContentKind.Item, "Key");
            ((ItemTemplate)item.Template).SpawnRoomBlueprintId = roomA.BlueprintId;
            await catalog.SaveAsync(item);

            var mob = catalog.CreateNew(ContentKind.Mob, "Guard");
            ((MobTemplate)mob.Template).SpawnRoomBlueprintId = roomA.BlueprintId;
            await catalog.SaveAsync(mob);

            Assert.Empty(index.SweepBroken());
        }

        // ── BrokenFor — per-definition dangling refs ─────────────────────────────────

        [Fact]
        public void BrokenFor_ReturnsDanglingRefs_ForInMemoryRoomWithBadAreaId()
        {
            var (index, _) = NewFixture();

            // In-memory room referencing a nonexistent area (nothing written to disk)
            var room = new RoomTemplate("room.adhoc.test")
            {
                Name = "Test Room",
                AreaId = "area.adhoc.nonexistent",
            };

            var broken = index.BrokenFor(room);

            Assert.Single(broken);
            Assert.Equal("AreaId", broken[0].FieldLabel);
            Assert.Equal("area.adhoc.nonexistent", broken[0].MissingTargetId);
            Assert.Equal(ContentKind.Room, broken[0].SourceKind);
        }

        [Fact]
        public async Task BrokenFor_ReturnsEmpty_WhenAllRefsResolve()
        {
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "Existing Area");
            await catalog.SaveAsync(area);

            var room = new RoomTemplate("room.adhoc.check")
            {
                Name = "Checked Room",
                AreaId = area.BlueprintId,
            };

            var broken = index.BrokenFor(room);

            Assert.Empty(broken);
        }

        [Fact]
        public void BrokenFor_ReturnsDanglingRefs_ForInMemoryItemWithBadSpawnRoom()
        {
            var (index, _) = NewFixture();

            var item = new ItemTemplate("item.adhoc.test")
            {
                Name = "Lost Gem",
                SpawnRoomBlueprintId = "room.adhoc.nowhere",
            };

            var broken = index.BrokenFor(item);

            Assert.Single(broken);
            Assert.Equal("SpawnRoomBlueprintId", broken[0].FieldLabel);
            Assert.Equal("room.adhoc.nowhere", broken[0].MissingTargetId);
            Assert.Equal(ContentKind.Item, broken[0].SourceKind);
        }

        [Fact]
        public void BrokenFor_ReturnsDanglingRefs_ForInMemoryMobWithBadSpawnRoom()
        {
            var (index, _) = NewFixture();

            var mob = new MobTemplate("mob.adhoc.test")
            {
                Name = "Ghost",
                SpawnRoomBlueprintId = "room.adhoc.phantom",
            };

            var broken = index.BrokenFor(mob);

            Assert.Single(broken);
            Assert.Equal("SpawnRoomBlueprintId", broken[0].FieldLabel);
            Assert.Equal("room.adhoc.phantom", broken[0].MissingTargetId);
            Assert.Equal(ContentKind.Mob, broken[0].SourceKind);
        }

        [Fact]
        public void BrokenFor_ReturnsDanglingRefs_ForRoomWithBadExits()
        {
            var (index, _) = NewFixture();

            var room = new RoomTemplate("room.adhoc.hall")
            {
                Name = "Dead-End Hall",
            };
            room.Exits[Direction.West] = "room.adhoc.missing-west";

            var broken = index.BrokenFor(room);

            Assert.Single(broken);
            Assert.Equal("Exits[West]", broken[0].FieldLabel);
            Assert.Equal("room.adhoc.missing-west", broken[0].MissingTargetId);
            Assert.Equal(ContentKind.Room, broken[0].SourceKind);
        }

        // ── Edge: empty fields produce no referrers / no broken ──────────────────────

        [Fact]
        public async Task SweepBroken_DoesNotFlagBlankFields()
        {
            var (index, catalog) = NewFixture();

            // Room with empty AreaId — not a broken reference
            var room = catalog.CreateNew(ContentKind.Room, "Placeless");
            await catalog.SaveAsync(room);

            // Item with empty SpawnRoomBlueprintId — not a broken reference
            var item = catalog.CreateNew(ContentKind.Item, "Floating Orb");
            await catalog.SaveAsync(item);

            Assert.Empty(index.SweepBroken());
        }

        // ── Fifth edge: (Area, Rooms[]) → Room ───────────────────────────────────────

        [Fact]
        public async Task Referrers_MatchesArea_ViaRoomsList()
        {
            // An area whose Rooms list contains a room blueprint id → Referrers(Room, X) returns that area.
            var (index, catalog) = NewFixture();

            var room = catalog.CreateNew(ContentKind.Room, "Cave");
            await catalog.SaveAsync(room);

            var area = catalog.CreateNew(ContentKind.Area, "Dungeon");
            ((AreaTemplate)area.Template).Rooms.Add(room.BlueprintId);
            await catalog.SaveAsync(area);

            var referrers = index.Referrers(ContentKind.Room, room.BlueprintId);

            Assert.Contains(referrers, r =>
                r.ReferrerKind == ContentKind.Area
                && r.ReferrerBlueprintId == area.BlueprintId
                && r.FieldLabel == "Rooms[]");
        }

        [Fact]
        public async Task SweepBroken_FlagsArea_WithNonexistentRoomInRoomsList()
        {
            // An area listing a room blueprint id that has no file on disk → broken reference.
            var (index, catalog) = NewFixture();

            var area = catalog.CreateNew(ContentKind.Area, "Ghost Dungeon");
            ((AreaTemplate)area.Template).Rooms.Add("room.adhoc.nonexistent");
            await catalog.SaveAsync(area);

            var broken = index.SweepBroken();

            Assert.Contains(broken, b =>
                b.SourceKind == ContentKind.Area
                && b.SourceBlueprintId == area.BlueprintId
                && b.FieldLabel == "Rooms[]"
                && b.MissingTargetId == "room.adhoc.nonexistent");
        }

        [Fact]
        public async Task SweepBroken_DoesNotFlag_AreaWithAllRoomsPresent()
        {
            // An area whose Rooms list all resolve → no broken references from Rooms[].
            var (index, catalog) = NewFixture();

            var room = catalog.CreateNew(ContentKind.Room, "Resolved Room");
            await catalog.SaveAsync(room);

            var area = catalog.CreateNew(ContentKind.Area, "Clean Area");
            ((AreaTemplate)area.Template).Rooms.Add(room.BlueprintId);
            await catalog.SaveAsync(area);

            var broken = index.SweepBroken();

            Assert.DoesNotContain(broken, b =>
                b.SourceKind == ContentKind.Area && b.FieldLabel == "Rooms[]");
        }
    }
}
