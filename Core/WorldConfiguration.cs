using System;
using System.Threading;

namespace Hedron.Core
{
    /// <summary>
    /// Runtime configuration set by the world bootstrap before the host starts accepting connections.
    /// </summary>
    public class WorldConfiguration
    {
        private ResolvedStartingRoom _startingRoom = ResolvedStartingRoom.Unresolved;

        /// <summary>
        /// The resolved starting room, published by <c>WorldContentLoader.ResolveStartingRoom</c> as one
        /// immutable snapshot (startup, <c>reload</c>, <c>startroom</c>). A caller needing both the entity
        /// id and the blueprint id reads this property <b>once</b> and uses that snapshot (INV-31).
        /// </summary>
        public ResolvedStartingRoom StartingRoom
        {
            get => Volatile.Read(ref _startingRoom);
            set => Volatile.Write(ref _startingRoom, value ?? throw new ArgumentNullException(nameof(value)));
        }

        /// <summary>
        /// Entity id of the resolved starting room. Single-field read-through of <see cref="StartingRoom"/>;
        /// never combine with <see cref="StartingRoomBlueprintId"/> — take one <see cref="StartingRoom"/> read instead.
        /// </summary>
        public uint StartingRoomEntityId => StartingRoom.EntityId;

        /// <summary>
        /// Blueprint id of the resolved starting room. Single-field read-through of <see cref="StartingRoom"/>;
        /// never combine with <see cref="StartingRoomEntityId"/> — take one <see cref="StartingRoom"/> read instead.
        /// </summary>
        public string? StartingRoomBlueprintId => StartingRoom.BlueprintId;
    }
}
