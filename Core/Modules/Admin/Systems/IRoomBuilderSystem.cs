using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Domain system for runtime room authoring. All methods mutate entity/component state only;
    /// event publication is the caller's responsibility.
    /// </summary>
    public interface IRoomBuilderSystem
    {
        RoomCreationResult CreateRoom(string name, string description = "");
        void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional);
        void SetRoomName(uint roomId, string name);
        void SetRoomDescription(uint roomId, string description);
    }

    /// <summary>Result of <see cref="IRoomBuilderSystem.CreateRoom"/>.</summary>
    public readonly record struct RoomCreationResult(uint RoomEntityId, string BlueprintId);
}
