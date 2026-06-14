using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Default <see cref="IContentGenerationSystem"/>. Composes the four reused
    /// <c>I*ContentWriter</c>s and the <c>*Template</c> types into a connected, walkable swath of
    /// world content driven by a per-run seeded <see cref="IRandom"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Topology (Resolved Decision 3).</b> Each area's rooms form a linear east/west chain
    /// (room <c>i</c> ↔ room <c>i+1</c>), and consecutive areas are joined (the previous area's last
    /// room links up/down to the next area's first room), so the whole generated world is one
    /// walkable graph for scaling tests.
    /// </para>
    /// <para>
    /// <b>Determinism (INV-26).</b> Every choice — room counts, placement, stat rolls, aspect
    /// selection — rolls through the seeded <see cref="IRandom"/>; blueprint ids are derived from the
    /// profile prefix + a monotonic counter, never <c>Guid</c>. No wall-clock or ambient state is
    /// read, so a fixed-seed run is byte-reproducible within a runtime image.
    /// </para>
    /// </remarks>
    public sealed class ContentGenerationSystem : IContentGenerationSystem
    {
        private readonly IAreaContentWriter _areaWriter;
        private readonly IRoomContentWriter _roomWriter;
        private readonly IItemContentWriter _itemWriter;
        private readonly IMobContentWriter _mobWriter;

        public ContentGenerationSystem(
            IAreaContentWriter areaWriter,
            IRoomContentWriter roomWriter,
            IItemContentWriter itemWriter,
            IMobContentWriter mobWriter)
        {
            _areaWriter = areaWriter;
            _roomWriter = roomWriter;
            _itemWriter = itemWriter;
            _mobWriter = mobWriter;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationProfile profile, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(profile);

            // Deterministic per-run randomness from the profile seed (Resolved Decision 1, INV-26).
            IRandom rng = new SeededRandom(profile.Seed);

            var prefix = string.IsNullOrEmpty(profile.BlueprintPrefix) ? "gen." : profile.BlueprintPrefix;
            var counters = new BlueprintIdCounters(prefix);

            var areaTemplates = new List<AreaTemplate>();
            var roomTemplates = new List<RoomTemplate>();
            var mobTemplates = new List<MobTemplate>();
            var itemTemplates = new List<ItemTemplate>();

            // Tracks the last room of the previously generated area, to join areas into one graph.
            RoomTemplate? previousAreaLastRoom = null;

            var areaCount = Math.Max(0, profile.AreaCount);
            for (var a = 0; a < areaCount; a++)
            {
                var areaId = counters.NextArea();
                var area = new AreaTemplate(areaId)
                {
                    AreaId = areaId,
                    Name = $"Generated Area {a + 1:D4}",
                    Description = "Procedurally generated for bulk content testing.",
                };

                var affinity = PickAspect(profile, rng);
                if (affinity is { } aspect)
                    area.AspectAffinities = new Dictionary<AspectId, int> { [aspect] = 100 };

                var roomCount = NextInRange(rng, profile.RoomsPerArea);
                var areaRooms = new List<RoomTemplate>(roomCount);
                for (var r = 0; r < roomCount; r++)
                {
                    var roomId = counters.NextRoom();
                    var room = new RoomTemplate(roomId)
                    {
                        Name = $"Generated Room {roomId}",
                        Description = "A procedurally generated room.",
                        AreaId = areaId,
                    };
                    area.Rooms.Add(roomId);
                    areaRooms.Add(room);
                    roomTemplates.Add(room);
                }

                // Wire the area's rooms into an east/west chain (Resolved Decision 3).
                for (var r = 0; r + 1 < areaRooms.Count; r++)
                {
                    areaRooms[r].Exits[Direction.East] = areaRooms[r + 1].BlueprintId;
                    areaRooms[r + 1].Exits[Direction.West] = areaRooms[r].BlueprintId;
                }

                // Join this area to the previous one so all areas are reachable.
                if (previousAreaLastRoom is not null && areaRooms.Count > 0)
                {
                    previousAreaLastRoom.Exits[Direction.Down] = areaRooms[0].BlueprintId;
                    areaRooms[0].Exits[Direction.Up] = previousAreaLastRoom.BlueprintId;
                }
                if (areaRooms.Count > 0)
                    previousAreaLastRoom = areaRooms[^1];

                // Populate each room with mobs/items by density, scaled to a per-area level.
                var level = NextInRange(rng, profile.LevelRange);
                foreach (var room in areaRooms)
                {
                    var mobsHere = RollCount(rng, profile.MobDensity);
                    for (var m = 0; m < mobsHere; m++)
                        mobTemplates.Add(BuildMob(counters, room, level, profile.Scaling, rng));

                    var itemsHere = RollCount(rng, profile.ItemDensity);
                    for (var i = 0; i < itemsHere; i++)
                        itemTemplates.Add(BuildItem(counters, room, rng));
                }

                areaTemplates.Add(area);
            }

            // Write YAML through the reused writers (atomic tmp→rename inside each). No live world.
            foreach (var area in areaTemplates)
                await _areaWriter.WriteAsync(area, ct).ConfigureAwait(false);
            foreach (var room in roomTemplates)
                await _roomWriter.WriteAsync(room, ct).ConfigureAwait(false);
            foreach (var mob in mobTemplates)
                await _mobWriter.WriteAsync(mob, ct).ConfigureAwait(false);
            foreach (var item in itemTemplates)
                await _itemWriter.WriteAsync(item, ct).ConfigureAwait(false);

            var blueprintIds = new List<string>(
                areaTemplates.Count + roomTemplates.Count + mobTemplates.Count + itemTemplates.Count);
            foreach (var t in areaTemplates) blueprintIds.Add(t.BlueprintId);
            foreach (var t in roomTemplates) blueprintIds.Add(t.BlueprintId);
            foreach (var t in mobTemplates) blueprintIds.Add(t.BlueprintId);
            foreach (var t in itemTemplates) blueprintIds.Add(t.BlueprintId);

            return new GenerationResult
            {
                AreasWritten = areaTemplates.Count,
                RoomsWritten = roomTemplates.Count,
                MobsWritten = mobTemplates.Count,
                ItemsWritten = itemTemplates.Count,
                BlueprintIds = blueprintIds,
            };
        }

        private static MobTemplate BuildMob(
            BlueprintIdCounters counters, RoomTemplate room, int level, ScalingCurve scaling, IRandom rng)
        {
            var id = counters.NextMob();
            // Small deterministic stat jitter so sibling mobs are not byte-identical.
            var jitter = rng.Next(0, 11);
            return new MobTemplate(id)
            {
                Name = $"Generated Mob {id}",
                Description = "A procedurally generated creature.",
                Keywords = new List<string> { "generated", "creature" },
                MobType = MobType.Creature,
                SpawnRoomBlueprintId = room.BlueprintId,
                Level = level,
                MaxHp = scaling.HpForLevel(level) + jitter,
                Mind = scaling.StatForLevel(level),
                Body = scaling.StatForLevel(level),
                Spirit = scaling.StatForLevel(level),
                Attunement = scaling.StatForLevel(level),
            };
        }

        private static ItemTemplate BuildItem(BlueprintIdCounters counters, RoomTemplate room, IRandom rng)
        {
            var id = counters.NextItem();
            return new ItemTemplate(id)
            {
                Name = $"Generated Item {id}",
                Description = "A procedurally generated item.",
                Keywords = new List<string> { "generated", "item" },
                ItemType = ItemType.Misc,
                SpawnRoomBlueprintId = room.BlueprintId,
                StatBonuses = new List<EquipmentStatBonus> { new(ScoreId.AttackPower, rng.Next(0, 5)) },
            };
        }

        /// <summary>Inclusive draw from a <c>(Min, Max)</c> range, tolerant of an inverted range.</summary>
        private static int NextInRange(IRandom rng, (int Min, int Max) range)
        {
            var min = Math.Min(range.Min, range.Max);
            var max = Math.Max(range.Min, range.Max);
            return rng.Next(min, max + 1);
        }

        /// <summary>
        /// Turns a fractional density into a concrete count: the integer floor always lands, and the
        /// fractional remainder is a probability for one extra — rolled through the seam.
        /// </summary>
        private static int RollCount(IRandom rng, double density)
        {
            if (density <= 0) return 0;
            var whole = (int)Math.Floor(density);
            var frac = density - whole;
            if (frac > 0 && rng.NextDouble() < frac)
                whole++;
            return whole;
        }

        /// <summary>Weighted choice over the profile's aspect mix; null when the mix is empty.</summary>
        private static AspectId? PickAspect(GenerationProfile profile, IRandom rng)
        {
            var mix = profile.AspectMix;
            if (mix is null || mix.Count == 0) return null;

            var total = 0;
            foreach (var entry in mix)
                if (entry.Weight > 0) total += entry.Weight;
            if (total <= 0) return null;

            var roll = rng.Next(0, total);
            foreach (var entry in mix)
            {
                if (entry.Weight <= 0) continue;
                roll -= entry.Weight;
                if (roll < 0) return entry.Aspect;
            }
            return mix[^1].Aspect;
        }

        /// <summary>
        /// Mints deterministic, zero-padded blueprint ids per kind from the profile prefix and a
        /// monotonic per-kind counter — the reproducibility-critical replacement for the
        /// <c>mk*</c> builders' <c>Guid</c> ids.
        /// </summary>
        private sealed class BlueprintIdCounters
        {
            private readonly string _prefix;
            private int _area;
            private int _room;
            private int _mob;
            private int _item;

            public BlueprintIdCounters(string prefix) => _prefix = prefix;

            public string NextArea() => $"{_prefix}area.{++_area:D4}";
            public string NextRoom() => $"{_prefix}room.{++_room:D4}";
            public string NextMob() => $"{_prefix}mob.{++_mob:D4}";
            public string NextItem() => $"{_prefix}item.{++_item:D4}";
        }
    }

    /// <summary>Translates a <see cref="ScalingCurve"/> + level into concrete mob stat numbers.</summary>
    internal static class ScalingCurveExtensions
    {
        public static int HpForLevel(this ScalingCurve curve, int level)
        {
            var l = Math.Max(1, level);
            return curve switch
            {
                ScalingCurve.Quadratic => 50 + l * l * 2,
                _ => 50 + l * 10,
            };
        }

        public static int StatForLevel(this ScalingCurve curve, int level)
        {
            var l = Math.Max(1, level);
            return curve switch
            {
                ScalingCurve.Quadratic => 10 + l * l,
                _ => 10 + l * 2,
            };
        }
    }
}
