using System.Collections.Generic;

namespace Hedron.Core.Modules.World.Systems
{
    public interface IAreaSystem
    {
        IReadOnlyList<uint> GetRoomsInArea(uint areaEntityId);
        uint? GetAreaForRoom(uint roomEntityId);
        void AssignRoomToArea(uint roomEntityId, uint areaEntityId, string areaBlueprintId);
    }
}
