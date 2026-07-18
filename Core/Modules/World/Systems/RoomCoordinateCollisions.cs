using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.World.Templates;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// One authoring-grid cell occupied by more than one room in the same area. Advisory only —
    /// coordinates are not a runtime constraint this slice (see <c>RoomTemplate.X/Y/Z</c>).
    /// </summary>
    public sealed record CoordinateCollision(
        string AreaId,
        int X,
        int Y,
        int Z,
        IReadOnlyList<string> RoomBlueprintIds);

    /// <summary>
    /// Pure grouping of room templates by <c>(AreaId, X, Y, Z)</c> — the single detection consumed
    /// by both <see cref="IContentValidator"/>'s registry-mode collision warning and the visual
    /// grid editor's layout proposal. Rooms missing any coordinate or a blank <c>AreaId</c> are
    /// excluded (nothing to collide against).
    /// </summary>
    public static class RoomCoordinateCollisions
    {
        public static IReadOnlyList<CoordinateCollision> Find(IEnumerable<RoomTemplate> rooms)
        {
            var groups = new Dictionary<(string AreaId, int X, int Y, int Z), List<string>>();

            foreach (var room in rooms)
            {
                if (string.IsNullOrEmpty(room.AreaId))
                    continue;
                if (room.X is not { } x || room.Y is not { } y || room.Z is not { } z)
                    continue;

                var key = (room.AreaId, x, y, z);
                if (!groups.TryGetValue(key, out var ids))
                    groups[key] = ids = new List<string>();
                ids.Add(room.BlueprintId);
            }

            return groups
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => new CoordinateCollision(
                    kv.Key.AreaId, kv.Key.X, kv.Key.Y, kv.Key.Z,
                    kv.Value.OrderBy(id => id, System.StringComparer.Ordinal).ToList()))
                .OrderBy(c => c.AreaId, System.StringComparer.Ordinal)
                .ThenBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z)
                .ToList();
        }
    }
}
