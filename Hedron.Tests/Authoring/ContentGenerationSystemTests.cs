using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// System-unit tests for <see cref="ContentGenerationSystem"/>. They compose the system over
    /// recording writers (which capture the emitted templates instead of touching disk), so the
    /// determinism, profile-count fidelity, and no-ambient-nondeterminism contracts can be asserted
    /// on the in-memory template stream.
    /// </summary>
    public sealed class ContentGenerationSystemTests
    {
        // ── Recording writers ─────────────────────────────────────────────────────────

        private sealed class RecordingAreaWriter : IAreaContentWriter
        {
            public List<AreaTemplate> Written { get; } = new();
            public Task WriteAsync(AreaTemplate template, CancellationToken ct = default)
            {
                Written.Add(template);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingRoomWriter : IRoomContentWriter
        {
            public List<RoomTemplate> Written { get; } = new();
            public Task WriteAsync(RoomTemplate template, CancellationToken ct = default)
            {
                Written.Add(template);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingItemWriter : IItemContentWriter
        {
            public List<ItemTemplate> Written { get; } = new();
            public Task WriteAsync(ItemTemplate template, CancellationToken ct = default)
            {
                Written.Add(template);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingMobWriter : IMobContentWriter
        {
            public List<MobTemplate> Written { get; } = new();
            public Task WriteAsync(MobTemplate template, CancellationToken ct = default)
            {
                Written.Add(template);
                return Task.CompletedTask;
            }
        }

        private sealed record Harness(
            ContentGenerationSystem System,
            RecordingAreaWriter Areas,
            RecordingRoomWriter Rooms,
            RecordingItemWriter Items,
            RecordingMobWriter Mobs);

        private static Harness NewSystem()
        {
            var areas = new RecordingAreaWriter();
            var rooms = new RecordingRoomWriter();
            var items = new RecordingItemWriter();
            var mobs = new RecordingMobWriter();
            var system = new ContentGenerationSystem(areas, rooms, items, mobs);
            return new Harness(system, areas, rooms, items, mobs);
        }

        private static GenerationProfile SampleProfile(int seed = 1234) => new()
        {
            Seed = seed,
            AreaCount = 3,
            RoomsPerArea = (3, 5),
            LevelRange = (1, 10),
            MobDensity = 1.5,
            ItemDensity = 0.75,
            AspectMix = new List<AspectMixEntry>
            {
                new(AspectId.Fire, 3),
                new(AspectId.Ice, 2),
            },
            Scaling = ScalingCurve.Linear,
            BlueprintPrefix = "gen.",
        };

        // ── Tests ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ContentGeneration_SameSeed_IsDeterministic()
        {
            var profile = SampleProfile(seed: 99);

            var first = NewSystem();
            var firstResult = await first.System.GenerateAsync(profile);

            var second = NewSystem();
            var secondResult = await second.System.GenerateAsync(profile);

            // Result-level: identical counts and identical ordered blueprint-id lists.
            Assert.Equal(firstResult.AreasWritten, secondResult.AreasWritten);
            Assert.Equal(firstResult.RoomsWritten, secondResult.RoomsWritten);
            Assert.Equal(firstResult.MobsWritten, secondResult.MobsWritten);
            Assert.Equal(firstResult.ItemsWritten, secondResult.ItemsWritten);
            Assert.Equal(firstResult.BlueprintIds, secondResult.BlueprintIds);

            // Writer-input level: the captured templates match too (mob stats, exits, affinities).
            Assert.Equal(
                first.Mobs.Written.Select(m => (m.BlueprintId, m.Level, m.MaxHp, m.SpawnRoomBlueprintId)),
                second.Mobs.Written.Select(m => (m.BlueprintId, m.Level, m.MaxHp, m.SpawnRoomBlueprintId)));
            Assert.Equal(
                first.Items.Written.Select(i => (i.BlueprintId, StatBonusKey(i), i.SpawnRoomBlueprintId)),
                second.Items.Written.Select(i => (i.BlueprintId, StatBonusKey(i), i.SpawnRoomBlueprintId)));
            Assert.Equal(
                first.Areas.Written.Select(AspectKey),
                second.Areas.Written.Select(AspectKey));
        }

        private static string StatBonusKey(ItemTemplate item) =>
            string.Join(",", item.StatBonuses.Select(b => $"{b.TargetScore}:{b.Magnitude}"));

        private static string AspectKey(AreaTemplate area) =>
            area.AspectAffinities is { Count: > 0 }
                ? string.Join(",", area.AspectAffinities.Select(kv => $"{kv.Key}:{kv.Value}"))
                : "(none)";

        [Fact]
        public async Task ContentGeneration_DifferentSeed_DiffersStructurally()
        {
            var a = NewSystem();
            var resultA = await a.System.GenerateAsync(SampleProfile(seed: 1));

            var b = NewSystem();
            var resultB = await b.System.GenerateAsync(SampleProfile(seed: 2));

            // Blueprint-id sets are derived from counters so they may overlap, but the structural
            // placements (room counts, mob/item placements, affinities) should differ for two seeds.
            var structureA = (resultA.RoomsWritten, resultA.MobsWritten, resultA.ItemsWritten,
                string.Join("|", a.Areas.Written.Select(AspectKey)));
            var structureB = (resultB.RoomsWritten, resultB.MobsWritten, resultB.ItemsWritten,
                string.Join("|", b.Areas.Written.Select(AspectKey)));

            Assert.NotEqual(structureA, structureB);
        }

        [Fact]
        public async Task ContentGeneration_RespectsProfileCounts()
        {
            var profile = new GenerationProfile
            {
                Seed = 7,
                AreaCount = 4,
                RoomsPerArea = (2, 2),   // exactly 2 rooms each → 8 rooms
                LevelRange = (5, 5),
                MobDensity = 0,          // no mobs
                ItemDensity = 0,         // no items
                BlueprintPrefix = "gen.",
            };

            var h = NewSystem();
            var result = await h.System.GenerateAsync(profile);

            Assert.Equal(4, result.AreasWritten);
            Assert.Equal(4, h.Areas.Written.Count);
            Assert.Equal(8, result.RoomsWritten);
            Assert.Equal(8, h.Rooms.Written.Count);
            Assert.Equal(0, result.MobsWritten);
            Assert.Equal(0, result.ItemsWritten);
            Assert.Empty(h.Mobs.Written);
            Assert.Empty(h.Items.Written);

            // Every generated mob level honors the (5,5) range.
            Assert.All(h.Mobs.Written, m => Assert.Equal(5, m.Level));
        }

        [Fact]
        public async Task ContentGeneration_RollsThroughInjectedRandom_NoAmbientNondeterminism()
        {
            // No FakeRandom is injectable (the system seeds its own SeededRandom from the profile),
            // so the INV-26 seam assertion is: with a fixed profile, repeated runs are identical
            // regardless of wall-clock/ambient state between them. Any Guid/DateTime/Random.Shared
            // leak would break this.
            var profile = SampleProfile(seed: 555);

            var run1 = NewSystem();
            var r1 = await run1.System.GenerateAsync(profile);

            var run2 = NewSystem();
            var r2 = await run2.System.GenerateAsync(profile);

            Assert.Equal(r1.BlueprintIds, r2.BlueprintIds);
            Assert.Equal(
                run1.Mobs.Written.Select(m => m.MaxHp),
                run2.Mobs.Written.Select(m => m.MaxHp));
        }

        [Fact]
        public async Task ContentGeneration_EmitsConnectedRoomGraph()
        {
            // Resolved Decision 3: rooms form a walkable graph. Assert each area's rooms chain and
            // consecutive areas are joined, so every room is reachable from the first.
            var profile = new GenerationProfile
            {
                Seed = 3,
                AreaCount = 2,
                RoomsPerArea = (3, 3),
                LevelRange = (1, 1),
                BlueprintPrefix = "gen.",
            };

            var h = NewSystem();
            await h.System.GenerateAsync(profile);

            var rooms = h.Rooms.Written.ToDictionary(r => r.BlueprintId);
            var start = h.Rooms.Written[0].BlueprintId;

            var visited = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!visited.Add(id)) continue;
                foreach (var target in rooms[id].Exits.Values)
                    if (rooms.ContainsKey(target) && !visited.Contains(target))
                        stack.Push(target);
            }

            // All 6 rooms reachable from the first room across both areas.
            Assert.Equal(rooms.Count, visited.Count);
        }
    }
}
