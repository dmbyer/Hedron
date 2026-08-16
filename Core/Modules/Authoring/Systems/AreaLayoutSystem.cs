using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Default <see cref="IAreaLayoutSystem"/>. BFS over the exit graph, seeded from anchored
    /// (fully-coordinate) rooms in ordinal blueprint-id order, direction enumerated in
    /// <see cref="Direction"/> declaration order for determinism. Only exits whose target is
    /// another room in the same area participate in placement — non-adjacent, cross-area, and
    /// self-loop exits are the grid's badge/edge presentation concern, not a layout input.
    /// </summary>
    public sealed class AreaLayoutSystem : IAreaLayoutSystem
    {
        private readonly IContentDefinitionCatalog _catalog;

        public AreaLayoutSystem(IContentDefinitionCatalog catalog)
        {
            _catalog = catalog;
        }

        public AreaLayoutProposal Propose(string areaBlueprintId)
        {
            var rooms = LoadAreaRooms(areaBlueprintId);

            var anchored = new Dictionary<string, RoomPosition>(StringComparer.Ordinal);
            var anchoredTemplates = new List<RoomTemplate>();
            var coordless = new List<RoomTemplate>();

            foreach (var room in rooms.Values)
            {
                if (room.X is { } x && room.Y is { } y && room.Z is { } z)
                {
                    anchored[room.BlueprintId] = new RoomPosition(x, y, z);
                    anchoredTemplates.Add(room);
                }
                else
                {
                    coordless.Add(room);
                }
            }

            var occupied = new HashSet<(int X, int Y, int Z)>(anchored.Values.Select(p => (p.X, p.Y, p.Z)));
            var proposed = new Dictionary<string, RoomPosition>(StringComparer.Ordinal);
            var placed = new HashSet<string>(anchored.Keys, StringComparer.Ordinal);

            // Multi-source BFS seeded from anchors, ordinal blueprint-id order.
            var queue = new Queue<RoomTemplate>(anchoredTemplates.OrderBy(r => r.BlueprintId, StringComparer.Ordinal));
            while (queue.Count > 0)
            {
                var room = queue.Dequeue();
                var pos = anchored.TryGetValue(room.BlueprintId, out var anchoredPos)
                    ? anchoredPos
                    : proposed[room.BlueprintId];

                foreach (var direction in DirectionOrder)
                {
                    if (!room.Exits.TryGetValue(direction, out var targetId))
                        continue;
                    if (!rooms.TryGetValue(targetId, out var target))
                        continue; // cross-area or dangling — not a placement input.
                    if (placed.Contains(targetId))
                        continue;

                    var offset = direction.Offset();
                    var candidate = (X: pos.X + offset.Dx, Y: pos.Y + offset.Dy, Z: pos.Z + offset.Dz);
                    var cell = occupied.Contains(candidate)
                        ? SpillToNearestFreeCell(candidate, occupied)
                        : candidate;

                    occupied.Add(cell);
                    var cellPos = new RoomPosition(cell.X, cell.Y, cell.Z);
                    proposed[targetId] = cellPos;
                    placed.Add(targetId);
                    queue.Enqueue(target);
                }
            }

            // Remaining coordless rooms are disconnected from every anchor (including the
            // no-anchors-at-all case). Each becomes its own component, rooted at the next
            // deterministic free origin, processed in ordinal blueprint-id order.
            foreach (var root in coordless.OrderBy(r => r.BlueprintId, StringComparer.Ordinal))
            {
                if (placed.Contains(root.BlueprintId))
                    continue;

                var origin = FindNearestFreeCell((0, 0, 0), occupied);
                occupied.Add(origin);
                proposed[root.BlueprintId] = new RoomPosition(origin.X, origin.Y, origin.Z);
                placed.Add(root.BlueprintId);

                var componentQueue = new Queue<RoomTemplate>();
                componentQueue.Enqueue(root);
                while (componentQueue.Count > 0)
                {
                    var room = componentQueue.Dequeue();
                    var pos = proposed[room.BlueprintId];

                    foreach (var direction in DirectionOrder)
                    {
                        if (!room.Exits.TryGetValue(direction, out var targetId))
                            continue;
                        if (!rooms.TryGetValue(targetId, out var target))
                            continue;
                        if (placed.Contains(targetId))
                            continue;

                        var offset = direction.Offset();
                        var candidate = (X: pos.X + offset.Dx, Y: pos.Y + offset.Dy, Z: pos.Z + offset.Dz);
                        var cell = occupied.Contains(candidate)
                            ? SpillToNearestFreeCell(candidate, occupied)
                            : candidate;

                        occupied.Add(cell);
                        proposed[targetId] = new RoomPosition(cell.X, cell.Y, cell.Z);
                        placed.Add(targetId);
                        componentQueue.Enqueue(target);
                    }
                }
            }

            var collisions = RoomCoordinateCollisions.Find(anchoredTemplates);

            return new AreaLayoutProposal(anchored, proposed, collisions);
        }

        public async Task<AreaLayoutApplyResult> ApplyProposalAsync(string areaBlueprintId, CancellationToken ct = default)
        {
            var proposal = Propose(areaBlueprintId);

            var written = 0;
            var warnings = new List<string>();

            foreach (var (blueprintId, pos) in proposal.Proposed)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var def = _catalog.Load(ContentKind.Room, blueprintId);
                    if (def is null)
                    {
                        warnings.Add($"Room '{blueprintId}' no longer exists on disk; skipped.");
                        continue;
                    }

                    var room = (RoomTemplate)def.Template;
                    if (room.X is not null && room.Y is not null && room.Z is not null)
                        continue; // already anchored since Propose ran — never rewritten.

                    room.X = pos.X;
                    room.Y = pos.Y;
                    room.Z = pos.Z;

                    var result = await _catalog.SaveRoomAsync(room, bidirectional: false, ct).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        warnings.Add($"Room '{blueprintId}': {string.Join("; ", result.Errors)}");
                        continue;
                    }

                    written++;
                    if (result.Warnings.Count > 0)
                        warnings.AddRange(result.Warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Room '{blueprintId}': {ex.Message}");
                }
            }

            return new AreaLayoutApplyResult(written, warnings);
        }

        public async Task<AreaConnectResult> ConnectAsync(
            string fromRoomBlueprintId,
            string toRoomBlueprintId,
            CancellationToken ct = default)
        {
            if (_catalog.Load(ContentKind.Room, fromRoomBlueprintId)?.Template is not RoomTemplate from)
                return AreaConnectResult.Rejected(AreaConnectOutcome.RoomNotFound);
            if (_catalog.Load(ContentKind.Room, toRoomBlueprintId)?.Template is not RoomTemplate to)
                return AreaConnectResult.Rejected(AreaConnectOutcome.RoomNotFound);

            // Positions come from the source room's area layout, so a coordless room connects at the
            // ghost cell the grid actually renders it in. A target laid out in a different area is
            // absent from this proposal and therefore not adjacent — which is the correct answer.
            var proposal = Propose(from.AreaId);
            if (!TryGetPosition(proposal, fromRoomBlueprintId, out var fromPos) ||
                !TryGetPosition(proposal, toRoomBlueprintId, out var toPos))
            {
                return AreaConnectResult.Rejected(AreaConnectOutcome.NotAdjacent);
            }

            var direction = DirectionExtensions.FromOffset(
                toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
            if (direction is not { } dir)
                return AreaConnectResult.Rejected(AreaConnectOutcome.NotAdjacent);

            from.Exits[dir] = toRoomBlueprintId;
            var write = await _catalog.SaveRoomAsync(from, bidirectional: true, ct).ConfigureAwait(false);

            return new AreaConnectResult(
                write.Success ? AreaConnectOutcome.Connected : AreaConnectOutcome.WriteFailed,
                write.Success ? dir : null,
                write.Errors,
                write.Warnings);
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        private static bool TryGetPosition(
            AreaLayoutProposal proposal, string roomBlueprintId, out RoomPosition position)
        {
            if (proposal.Anchored.TryGetValue(roomBlueprintId, out var anchored))
            {
                position = anchored;
                return true;
            }
            if (proposal.Proposed.TryGetValue(roomBlueprintId, out var proposed))
            {
                position = proposed;
                return true;
            }
            position = null!;
            return false;
        }

        private static readonly Direction[] DirectionOrder =
        {
            Direction.North, Direction.South, Direction.East,
            Direction.West, Direction.Up, Direction.Down,
        };

        private Dictionary<string, RoomTemplate> LoadAreaRooms(string areaBlueprintId)
        {
            var rooms = new Dictionary<string, RoomTemplate>(StringComparer.Ordinal);
            foreach (var summary in _catalog.RoomsInArea(areaBlueprintId))
            {
                if (_catalog.Load(ContentKind.Room, summary.BlueprintId)?.Template is RoomTemplate room)
                    rooms[room.BlueprintId] = room;
            }
            return rooms;
        }

        /// <summary>
        /// Nearest free cell to <paramref name="candidate"/>, searched on the same Z via an
        /// expanding Chebyshev ring scan with a fixed, deterministic per-ring enumeration order.
        /// </summary>
        private static (int X, int Y, int Z) SpillToNearestFreeCell(
            (int X, int Y, int Z) candidate, HashSet<(int X, int Y, int Z)> occupied) =>
            FindNearestFreeCell(candidate, occupied);

        private static (int X, int Y, int Z) FindNearestFreeCell(
            (int X, int Y, int Z) center, HashSet<(int X, int Y, int Z)> occupied)
        {
            for (var radius = 0; ; radius++)
            {
                foreach (var (dx, dy) in ChebyshevRing(radius))
                {
                    var cell = (X: center.X + dx, Y: center.Y + dy, Z: center.Z);
                    if (!occupied.Contains(cell))
                        return cell;
                }
            }
        }

        /// <summary>
        /// Deterministic enumeration of the cells at exactly Chebyshev distance
        /// <paramref name="radius"/> from the origin (row-major by <c>dy</c>, then <c>dx</c>).
        /// </summary>
        private static IEnumerable<(int Dx, int Dy)> ChebyshevRing(int radius)
        {
            if (radius == 0)
            {
                yield return (0, 0);
                yield break;
            }

            for (var dy = -radius; dy <= radius; dy++)
            {
                if (Math.Abs(dy) == radius)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                        yield return (dx, dy);
                }
                else
                {
                    yield return (-radius, dy);
                    yield return (radius, dy);
                }
            }
        }
    }
}
