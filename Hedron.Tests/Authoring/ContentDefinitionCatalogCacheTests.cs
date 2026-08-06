using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
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
    /// System-unit tests for <see cref="ContentDefinitionCatalog"/>'s in-memory index — the cache-hit
    /// bound, the per-kind sweep bound, whole-index invalidation at every mutator (including the
    /// definitions a write <em>cascades</em> to), the bulk-loop cost property, and the
    /// lost-invalidation race the generation guard exists to close.
    /// <para>
    /// Filesystem effects are counted through the injected <see cref="IContentFileReader"/> seam
    /// rather than asserted by mutating a temp directory behind the catalog's back — the latter
    /// would assert <em>stale</em> behavior and freeze the "no FileSystemWatcher" decision into a test.
    /// </para>
    /// </summary>
    public sealed class ContentDefinitionCatalogCacheTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        // ── The counting seam ───────────────────────────────────────────────────────

        /// <summary>
        /// Pass-through <see cref="IContentFileReader"/> that counts directory sweeps and file
        /// reads, plus an <see cref="AfterGetFiles"/> hook that lets a test interleave a write
        /// into the middle of a sweep deterministically (no sleeps, no thread timing).
        /// </summary>
        private sealed class CountingFileReader : IContentFileReader
        {
            private readonly IContentFileReader _inner = new ContentFileReader();

            public ConcurrentDictionary<string, int> DirectorySweeps { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public int FileReads;

            /// <summary>Runs after the directory listing is taken but before the sweep publishes.</summary>
            public Action? AfterGetFiles { get; set; }

            public int TotalSweeps => DirectorySweeps.Values.Sum();

            public int SweepsOf(string directory) =>
                DirectorySweeps.TryGetValue(directory, out var n) ? n : 0;

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

            public IReadOnlyList<string> GetFiles(string directory, string searchPattern)
            {
                DirectorySweeps.AddOrUpdate(directory, 1, (_, v) => v + 1);
                var files = _inner.GetFiles(directory, searchPattern);
                AfterGetFiles?.Invoke();
                return files;
            }

            public bool FileExists(string path) => _inner.FileExists(path);

            public string ReadAllText(string path)
            {
                Interlocked.Increment(ref FileReads);
                return _inner.ReadAllText(path);
            }
        }

        // ── Empty registry stubs for the validator ──────────────────────────────────

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

        private (ContentDefinitionCatalog Catalog, CountingFileReader Reader, string ContentDir) NewCatalog()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-catalog-cache-" + Guid.NewGuid().ToString("N"));
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
            var reader = new CountingFileReader();

            var catalog = new ContentDefinitionCatalog(
                serializer,
                validator,
                registry,
                new ContentReferenceIndex(serializer, options, NullLogger<ContentReferenceIndex>.Instance),
                new AreaContentWriter(options),
                new RoomContentWriter(options),
                new ItemContentWriter(options),
                new MobContentWriter(options),
                options,
                NullLogger<ContentDefinitionCatalog>.Instance,
                reader);

            return (catalog, reader, dir);
        }

        private static string DirectoryFor(string contentDir, ContentKind kind) =>
            Path.Combine(contentDir, kind.Subdirectory());

        private static async Task<string> SeedAsync(
            ContentDefinitionCatalog catalog, ContentKind kind, string name, Action<ContentDefinition>? configure = null)
        {
            var def = catalog.CreateNew(kind, name);
            configure?.Invoke(def);
            var result = await catalog.SaveAsync(def);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return def.BlueprintId;
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        // ── Cache hit ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task List_RepeatedWithNoInterveningWrite_PerformsNoFilesystemRead()
        {
            var (catalog, reader, _) = NewCatalog();
            await SeedAsync(catalog, ContentKind.Area, "Alpha");
            await SeedAsync(catalog, ContentKind.Area, "Beta");

            var first = catalog.List(ContentKind.Area);
            var sweepsAfterFirst = reader.TotalSweeps;
            var readsAfterFirst = reader.FileReads;

            var second = catalog.List(ContentKind.Area);
            var third = catalog.List(ContentKind.Area);

            Assert.Equal(sweepsAfterFirst, reader.TotalSweeps);
            Assert.Equal(readsAfterFirst, reader.FileReads);
            Assert.Equal(first.Count, second.Count);
            Assert.Equal(2, third.Count);
        }

        [Fact]
        public async Task RoomsInArea_RepeatedWithNoInterveningWrite_PerformsNoFilesystemRead()
        {
            var (catalog, reader, _) = NewCatalog();
            var areaId = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            await SeedAsync(catalog, ContentKind.Room, "Hall", d => ((RoomTemplate)d.Template).AreaId = areaId);

            Assert.Single(catalog.RoomsInArea(areaId));
            var sweeps = reader.TotalSweeps;
            var reads = reader.FileReads;

            Assert.Single(catalog.RoomsInArea(areaId));

            Assert.Equal(sweeps, reader.TotalSweeps);
            Assert.Equal(reads, reader.FileReads);
        }

        // ── Postcondition 3: per-kind sweep bound ───────────────────────────────────

        [Fact]
        public async Task ListingAllKinds_SweepsEachKindDirectory_AtMostOncePerInvalidation()
        {
            var (catalog, reader, contentDir) = NewCatalog();
            var areaId = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            var roomId = await SeedAsync(catalog, ContentKind.Room, "Hall", d => ((RoomTemplate)d.Template).AreaId = areaId);
            await SeedAsync(catalog, ContentKind.Item, "Sword", d => ((ItemTemplate)d.Template).SpawnRoomBlueprintId = roomId);
            await SeedAsync(catalog, ContentKind.Mob, "Rat", d => ((MobTemplate)d.Template).SpawnRoomBlueprintId = roomId);

            reader.DirectorySweeps.Clear();

            foreach (var _ in Enumerable.Range(0, 3))
            {
                foreach (var kind in new[] { ContentKind.Area, ContentKind.Room, ContentKind.Item, ContentKind.Mob })
                    catalog.List(kind);
            }

            foreach (var kind in new[] { ContentKind.Area, ContentKind.Room, ContentKind.Item, ContentKind.Mob })
            {
                Assert.Equal(1, reader.SweepsOf(DirectoryFor(contentDir, kind)));
            }

            // The room→area map backing item/mob two-hop resolution is derived from the room
            // summaries, not a second sweep of the room directory.
            Assert.Equal(4, reader.TotalSweeps);
        }

        // ── Bulk-loop cost ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Load_AfterInvalidation_ReadsOneFile_NotTheCorpus()
        {
            var (catalog, reader, _) = NewCatalog();
            var ids = new List<string>();
            for (var i = 0; i < 6; i++)
                ids.Add(await SeedAsync(catalog, ContentKind.Item, $"Item {i}"));

            catalog.List(ContentKind.Item);          // warm the summaries
            catalog.Load(ContentKind.Item, ids[0]);  // warm one body

            // A conformance-style loop: write one entry (invalidating), then load the next.
            var target = catalog.Load(ContentKind.Item, ids[1])!;
            await catalog.SaveAsync(target);

            var readsBefore = reader.FileReads;
            var reloaded = catalog.Load(ContentKind.Item, ids[2]);

            Assert.NotNull(reloaded);
            Assert.Equal(readsBefore + 1, reader.FileReads);
        }

        // ── Lost-invalidation race (the generation guard) ───────────────────────────

        [Fact]
        public async Task Write_LandingMidSweep_DoesNotPublishPreWriteState()
        {
            var (catalog, reader, _) = NewCatalog();
            await SeedAsync(catalog, ContentKind.Area, "Alpha");

            // The interleave: the sweep takes its directory listing, *then* a write completes and
            // invalidates, *then* the sweep publishes what it read before the write. This asserts
            // Postcondition 2 survives that ordering — the next read still observes the write.
            // (In the as-built design the late publish lands in the snapshot object Invalidate
            // already detached, so it is inert; the catalog's Generation check states that
            // invariant rather than being the only thing enforcing it.)
            string? lateId = null;
            var fired = false;
            reader.AfterGetFiles = () =>
            {
                if (fired) return;
                fired = true;
                var def = catalog.CreateNew(ContentKind.Area, "Beta");
                lateId = def.BlueprintId;
                catalog.SaveAsync(def).GetAwaiter().GetResult();
            };

            catalog.List(ContentKind.Area);
            reader.AfterGetFiles = null;

            var observed = catalog.List(ContentKind.Area).Select(s => s.BlueprintId).ToList();

            Assert.NotNull(lateId);
            Assert.Contains(lateId!, observed);
        }

        // ── Whole-index invalidation, one per mutating method ───────────────────────

        [Fact]
        public async Task SaveAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            var id = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            Assert.Equal("Alpha", catalog.List(ContentKind.Area).Single().Name);

            var def = catalog.Load(ContentKind.Area, id)!;
            ((AreaTemplate)def.Template).Name = "Renamed";
            await catalog.SaveAsync(def);

            Assert.Equal("Renamed", catalog.List(ContentKind.Area).Single().Name);
            Assert.Equal("Renamed", ((AreaTemplate)catalog.Load(ContentKind.Area, id)!.Template).Name);
        }

        [Fact]
        public async Task CreateAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            Assert.Empty(catalog.List(ContentKind.Item));

            var def = catalog.CreateNew(ContentKind.Item, "Sword", "item.blade");
            Assert.True((await catalog.CreateAsync(def)).Success);

            Assert.Equal("item.blade", catalog.List(ContentKind.Item).Single().BlueprintId);
            Assert.NotNull(catalog.Load(ContentKind.Item, "item.blade"));
        }

        [Fact]
        public async Task SaveRoomAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            var id = await SeedAsync(catalog, ContentKind.Room, "Hall");
            catalog.List(ContentKind.Room);

            var room = (RoomTemplate)catalog.Load(ContentKind.Room, id)!.Template;
            room.Name = "Great Hall";
            Assert.True((await catalog.SaveRoomAsync(room, bidirectional: false)).Success);

            Assert.Equal("Great Hall", catalog.List(ContentKind.Room).Single().Name);
        }

        [Fact]
        public async Task DeleteAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            var id = await SeedAsync(catalog, ContentKind.Mob, "Rat");
            Assert.Single(catalog.List(ContentKind.Mob));
            Assert.NotNull(catalog.Load(ContentKind.Mob, id));

            await catalog.DeleteAsync(ContentKind.Mob, id);

            Assert.Empty(catalog.List(ContentKind.Mob));
            Assert.Null(catalog.Load(ContentKind.Mob, id));
        }

        [Fact]
        public async Task RenameAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            var id = await SeedAsync(catalog, ContentKind.Item, "Sword");
            Assert.Equal(id, catalog.List(ContentKind.Item).Single().BlueprintId);

            var result = await catalog.RenameAsync(ContentKind.Item, id, "item.renamed");
            Assert.True(result.Success, string.Join("; ", result.Errors));

            Assert.Equal("item.renamed", catalog.List(ContentKind.Item).Single().BlueprintId);
            Assert.Null(catalog.Load(ContentKind.Item, id));
            Assert.NotNull(catalog.Load(ContentKind.Item, "item.renamed"));
        }

        [Fact]
        public async Task RemoveRoomExitAsync_InvalidatesIndex()
        {
            var (catalog, _, _) = NewCatalog();
            var northId = await SeedAsync(catalog, ContentKind.Room, "North");
            var southId = await SeedAsync(catalog, ContentKind.Room, "South", d =>
                ((RoomTemplate)d.Template).Exits[Direction.North] = "placeholder");

            var south = (RoomTemplate)catalog.Load(ContentKind.Room, southId)!.Template;
            south.Exits[Direction.North] = northId;
            await catalog.SaveRoomAsync(south, bidirectional: false);

            Assert.True(((RoomTemplate)catalog.Load(ContentKind.Room, southId)!.Template)
                .Exits.ContainsKey(Direction.North));

            await catalog.RemoveRoomExitAsync(southId, Direction.North, bidirectional: false);

            Assert.False(((RoomTemplate)catalog.Load(ContentKind.Room, southId)!.Template)
                .Exits.ContainsKey(Direction.North));
        }

        // ── Cascade invalidation — a definition the write reached, not just the target ──

        [Fact]
        public async Task RenameAsync_Cascade_ReferrerIsObservedOnNextRead()
        {
            var (catalog, _, _) = NewCatalog();
            var areaId = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            var roomId = await SeedAsync(catalog, ContentKind.Room, "Hall", d => ((RoomTemplate)d.Template).AreaId = areaId);

            // Warm both the room body and the summaries so a stale index would be visible.
            Assert.Equal(areaId, ((RoomTemplate)catalog.Load(ContentKind.Room, roomId)!.Template).AreaId);
            Assert.Single(catalog.RoomsInArea(areaId));

            var result = await catalog.RenameAsync(ContentKind.Area, areaId, "area.renamed");
            Assert.True(result.Success, string.Join("; ", result.Errors));

            Assert.Equal("area.renamed", ((RoomTemplate)catalog.Load(ContentKind.Room, roomId)!.Template).AreaId);
            Assert.Single(catalog.RoomsInArea("area.renamed"));
            Assert.Empty(catalog.RoomsInArea(areaId));
        }

        [Fact]
        public async Task DeleteAsync_Cascade_ClearedAreaIdIsObservedOnNextRead()
        {
            var (catalog, _, _) = NewCatalog();
            var areaId = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            var roomId = await SeedAsync(catalog, ContentKind.Room, "Hall", d => ((RoomTemplate)d.Template).AreaId = areaId);

            Assert.Equal(areaId, ((RoomTemplate)catalog.Load(ContentKind.Room, roomId)!.Template).AreaId);
            Assert.Single(catalog.RoomsInArea(areaId));

            await catalog.DeleteAsync(ContentKind.Area, areaId);

            Assert.Equal(string.Empty, ((RoomTemplate)catalog.Load(ContentKind.Room, roomId)!.Template).AreaId);
            Assert.Empty(catalog.RoomsInArea(areaId));
        }

        [Fact]
        public async Task SaveRoomAsync_Bidirectional_InverseExitOnTheOtherRoomIsObservedOnNextRead()
        {
            var (catalog, _, _) = NewCatalog();
            var northId = await SeedAsync(catalog, ContentKind.Room, "North");
            var southId = await SeedAsync(catalog, ContentKind.Room, "South");

            // Warm the *target* room's body — the definition the cascade will touch.
            Assert.Empty(((RoomTemplate)catalog.Load(ContentKind.Room, northId)!.Template).Exits);

            var south = (RoomTemplate)catalog.Load(ContentKind.Room, southId)!.Template;
            south.Exits[Direction.North] = northId;
            var result = await catalog.SaveRoomAsync(south, bidirectional: true);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            var north = (RoomTemplate)catalog.Load(ContentKind.Room, northId)!.Template;
            Assert.Equal(southId, north.Exits[Direction.South]);
        }

        [Fact]
        public async Task RoomsInArea_ReflectsAnAreaIdChange_InBothOldAndNewArea()
        {
            var (catalog, _, _) = NewCatalog();
            var oldArea = await SeedAsync(catalog, ContentKind.Area, "Alpha");
            var newArea = await SeedAsync(catalog, ContentKind.Area, "Beta");
            var roomId = await SeedAsync(catalog, ContentKind.Room, "Hall", d => ((RoomTemplate)d.Template).AreaId = oldArea);

            Assert.Single(catalog.RoomsInArea(oldArea));
            Assert.Empty(catalog.RoomsInArea(newArea));

            var room = (RoomTemplate)catalog.Load(ContentKind.Room, roomId)!.Template;
            room.AreaId = newArea;
            await catalog.SaveAsync(new ContentDefinition(ContentKind.Room, room));

            Assert.Empty(catalog.RoomsInArea(oldArea));
            Assert.Single(catalog.RoomsInArea(newArea));
        }

        // ── Explicit Invalidate: the out-of-process escape hatch ────────────────────

        [Fact]
        public async Task Invalidate_ForcesTheNextReadBackToDisk()
        {
            var (catalog, reader, _) = NewCatalog();
            await SeedAsync(catalog, ContentKind.Area, "Alpha");
            catalog.List(ContentKind.Area);

            var sweeps = reader.TotalSweeps;
            catalog.Invalidate();
            catalog.List(ContentKind.Area);

            Assert.True(reader.TotalSweeps > sweeps);
        }

        [Fact]
        public async Task Load_DoesNotHandOutASharedMutableTemplate()
        {
            var (catalog, _, _) = NewCatalog();
            var id = await SeedAsync(catalog, ContentKind.Area, "Alpha");

            var first = (AreaTemplate)catalog.Load(ContentKind.Area, id)!.Template;
            first.Name = "Edited in a form, never saved";

            var second = (AreaTemplate)catalog.Load(ContentKind.Area, id)!.Template;
            Assert.Equal("Alpha", second.Name);
        }
    }
}
