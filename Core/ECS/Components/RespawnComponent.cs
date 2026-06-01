namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Stores the player's respawn location as a stable blueprint id.
    /// On death, <c>IDeathSystem.Respawn</c> resolves <see cref="RoomBlueprintId"/> to a
    /// live room entity id (same resolution path as <c>CharacterHydrationHandler</c>).
    /// Stores the blueprint id rather than the runtime entity id because room entity ids
    /// are not stable across restarts (world content is fresh-spawned each boot).
    /// </summary>
    [Persistent]
    public sealed class RespawnComponent : IComponent
    {
        /// <summary>
        /// Stable blueprint id of the room where this entity respawns.
        /// Null means "use the world starting room" (fallback behaviour in <c>IDeathSystem.Respawn</c>).
        /// </summary>
        public string? RoomBlueprintId { get; set; }
    }
}
