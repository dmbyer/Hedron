using System.Text.Json.Serialization;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Tracks which room entity this entity is currently inside.
    /// Zero means the entity is not placed in any room (e.g. during login).
    /// </summary>
    [Persistent]
    public class LocationComponent : IComponent
    {
        /// <summary>
        /// Runtime entity ID of the current room. Resolved from <see cref="RoomBlueprintId"/> at
        /// startup by <c>CharacterHydrationHandler</c>. Not serialized to SQLite — changes every
        /// restart as rooms are fresh-spawned.
        /// </summary>
        [JsonIgnore]
        public uint RoomEntityId { get; set; }

        /// <summary>
        /// Stable cross-restart reference. The blueprint ID of the room this entity is in.
        /// Null for instanced rooms or entities that have not yet been placed.
        /// </summary>
        public string? RoomBlueprintId { get; set; }
    }
}
