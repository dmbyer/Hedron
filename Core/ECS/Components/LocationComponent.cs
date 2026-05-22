namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Tracks which room entity this entity is currently inside.
    /// Zero means the entity is not placed in any room (e.g. during login).
    /// </summary>
    [Persistent]
    public class LocationComponent : IComponent
    {
        public uint RoomEntityId { get; set; }
    }
}
