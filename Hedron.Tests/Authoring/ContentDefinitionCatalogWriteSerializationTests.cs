using System;
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
    /// Tier 1 — the catalog's write-serialization guard (authoring-api-surface WP2, INV-31).
    ///
    /// Since the authoring host also serves JSON endpoints, request threads are a second concurrent
    /// writer alongside the Blazor circuits. Every public mutator runs under one
    /// <c>SemaphoreSlim(1,1)</c>. These pin the two properties that has to hold: the re-entrant
    /// call path does not self-deadlock, and concurrent writers do not interleave into a corrupt
    /// result.
    /// </summary>
    public sealed class ContentDefinitionCatalogWriteSerializationTests : IDisposable
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
            var dir = Path.Combine(Path.GetTempPath(), "hedron-writegate-" + Guid.NewGuid().ToString("N"));
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
                serializer,
                validator,
                registry,
                new AreaContentWriter(options),
                new RoomContentWriter(options),
                new ItemContentWriter(options),
                new MobContentWriter(options),
                options,
                NullLogger<ContentDefinitionCatalog>.Instance);
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        /// <summary>
        /// Fails the test rather than hanging the suite if the gate ever self-deadlocks.
        /// </summary>
        private static async Task<T> WithinTimeout<T>(Task<T> task, string what)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(completed == task, $"{what} did not complete within 15s — the write gate deadlocked.");
            return await task;
        }

        // ── Re-entrancy ───────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateAsync_completes_without_deadlock_under_the_write_gate()
        {
            var catalog = NewCatalog();
            var definition = catalog.CreateNew(ContentKind.Room, "Gate Test", "room.gate");

            // CreateAsync is defined in terms of SaveAsync. A semaphore taken inside every public
            // mutator would deadlock exactly here — the specific defect the private-core shape exists
            // to prevent.
            var result = await WithinTimeout(catalog.CreateAsync(definition), nameof(catalog.CreateAsync));

            Assert.True(result.Success);
            Assert.NotNull(catalog.Load(ContentKind.Room, "room.gate"));
        }

        [Fact]
        public async Task Every_public_mutator_completes_in_sequence_without_deadlock()
        {
            var catalog = NewCatalog();

            var room = catalog.CreateNew(ContentKind.Room, "A", "room.a");
            Assert.True((await WithinTimeout(catalog.CreateAsync(room), "CreateAsync")).Success);

            var target = catalog.CreateNew(ContentKind.Room, "B", "room.b");
            Assert.True((await WithinTimeout(catalog.SaveAsync(target), "SaveAsync")).Success);

            var a = (RoomTemplate)catalog.Load(ContentKind.Room, "room.a")!.Template;
            a.Exits[Direction.East] = "room.b";
            Assert.True((await WithinTimeout(catalog.SaveRoomAsync(a, bidirectional: true), "SaveRoomAsync")).Success);

            Assert.True((await WithinTimeout(
                catalog.RemoveRoomExitAsync("room.a", Direction.East, bidirectional: true),
                "RemoveRoomExitAsync")).Success);

            Assert.True((await WithinTimeout(
                catalog.RenameAsync(ContentKind.Room, "room.b", "room.c"), "RenameAsync")).Success);

            await WithinTimeout(catalog.DeleteAsync(ContentKind.Room, "room.c"), "DeleteAsync");
            Assert.Null(catalog.Load(ContentKind.Room, "room.c"));
        }

        // ── Serialization ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Concurrent_creates_of_distinct_ids_all_land()
        {
            var catalog = NewCatalog();
            const int writers = 24;

            var results = await WithinTimeout(
                Task.WhenAll(Enumerable.Range(0, writers).Select(i => Task.Run(() =>
                    catalog.CreateAsync(catalog.CreateNew(ContentKind.Room, $"Room {i}", $"room.c{i}"))))),
                "concurrent CreateAsync");

            Assert.All(results, r => Assert.True(r.Success, string.Join("; ", r.Errors)));
            for (var i = 0; i < writers; i++)
                Assert.NotNull(catalog.Load(ContentKind.Room, $"room.c{i}"));
        }

        [Fact]
        public async Task Concurrent_creates_of_one_id_admit_exactly_one_winner()
        {
            var catalog = NewCatalog();

            // Unserialized, CreateAsync's "does it resolve on disk?" check and its write are a
            // TOCTOU: several racers can all observe "free" and all write. Under the gate exactly
            // one wins and the rest are refused with the collision error.
            var results = await WithinTimeout(
                Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
                    catalog.CreateAsync(catalog.CreateNew(ContentKind.Room, "Contested", "room.contested"))))),
                "contested CreateAsync");

            Assert.Single(results.Where(r => r.Success));
            Assert.All(
                results.Where(r => !r.Success),
                r => Assert.Contains(r.Errors, e => e.Contains("already exists")));
        }

        [Fact]
        public async Task A_concurrent_reader_never_observes_a_torn_definition()
        {
            var catalog = NewCatalog();
            var seed = catalog.CreateNew(ContentKind.Room, "Seed", "room.read");
            await catalog.CreateAsync(seed);

            var stop = false;
            var reader = Task.Run(() =>
            {
                var seen = 0;
                while (!Volatile.Read(ref stop))
                {
                    // Readers stay lock-free by design; what they must never see is a half-written
                    // template (a name from one write and coordinates from another).
                    if (catalog.Load(ContentKind.Room, "room.read")?.Template is RoomTemplate room)
                    {
                        // Either the seed state or one whole pass — never a name from one write
                        // paired with coordinates from another.
                        if (room.Name != "Seed")
                        {
                            Assert.StartsWith("Pass ", room.Name);
                            Assert.Equal(int.Parse(room.Name["Pass ".Length..]), room.X);
                            seen++;
                        }
                    }
                }
                return seen;
            });

            for (var i = 0; i < 40; i++)
            {
                var room = (RoomTemplate)catalog.Load(ContentKind.Room, "room.read")!.Template;
                room.Name = $"Pass {i}";
                room.X = i;
                await catalog.SaveRoomAsync(room, bidirectional: false);
            }

            Volatile.Write(ref stop, true);
            Assert.True(await reader > 0, "the reader observed nothing — the test proved nothing.");
        }
    }
}
