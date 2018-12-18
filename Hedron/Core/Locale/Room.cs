using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.System;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core
{

    public partial class Room : EntityContainer, ICopyableObject<Room>, ISpawnableObject
	{
		[JsonProperty]
		public RoomExits Exits       { get; private set; } = new RoomExits();

		[JsonProperty]
		public Tier      Tier        { get; private set; } = new Tier();

		[JsonProperty]
		public string    Name        { get; set; }

		[JsonProperty]
        public string    Description { get; set; }
		
		/// <summary>
		/// Creates a new room with default name and description. Must be added to cache.
		/// </summary>
        public Room() : base()
		{
			Name = "[name]";
			Description = "[description]";
		}

		/// <summary>
		/// Creates a new room and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype room</returns>
		public static Room NewPrototype(uint? prototypeID = null)
		{
			var newRoom = new Room();

			DataAccess.Add<Room>(newRoom, CacheType.Prototype, prototypeID);

			return newRoom;
		}

		/// <summary>
		/// Creates a new room and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced area</returns>
		public static Room NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<Room>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<Room>(DataAccess.Add<Room>(new Room(), CacheType.Instance), CacheType.Instance);
		}

		/// <summary>
		/// Points an exit to another room.
		/// </summary>
		/// <param name="exit">The direction to set</param>
		/// <param name="room">The ID of the room</param>
		public void SetExit(Constants.EXIT exit, uint? room)
        {
            switch (exit)
            {
                case Constants.EXIT.NORTH:
                    Exits.North = room;
                    return;
                case Constants.EXIT.EAST:
                    Exits.East = room;
                    return;
                case Constants.EXIT.SOUTH:
                    Exits.South = room;
                    return;
                case Constants.EXIT.WEST:
                    Exits.West = room;
                    return;
                case Constants.EXIT.UP:
                    Exits.Up = room;
                    return;
                case Constants.EXIT.DOWN:
                    Exits.Down = room;
                    return;
            }
		}

		/// <summary>
		/// Spawns an instance of the room from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent area instance ID</param>
		/// <returns>The spawned room. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits will all be null and must be fixed from prototype. Parent cannot be null. Adds new room to instanced area.</remarks>
		public T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T: CacheableObject
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the room from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent area instance ID</param>
		/// <returns>The instance ID of the spawned room. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits will all be null and must be fixed from prototype. Parent cannot be null. Adds new room to instanced area.</remarks>
		public uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			if (parent == null)
				throw new ArgumentNullException(nameof(parent), "Parent cannot be null when spawning a room.");
			
			Logger.Info(nameof(Room), nameof(Spawn), "Spawning room: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance room and add to parent area
			var newRoom = NewInstance(false);
			DataAccess.Get<Area>(parent, CacheType.Instance).AddEntity(newRoom.Instance, newRoom);

			// Set remaining properties
			newRoom.Prototype = Prototype;
			CopyTo(newRoom);

			// Spawn contained entities
			if (withEntities)
			{
				var entities = DataAccess.GetMany<ISpawnableObject>(_entityList, CacheType.Prototype);
				foreach (var entity in entities)
					entity.Spawn(withEntities, newRoom.Instance);
			}

			Logger.Info(nameof(Room), nameof(Spawn), "Finished spawning room.");

			return newRoom.Instance;
		}

		/// <summary>
		/// Copies this room's properties to another room.
		/// </summary>
		/// <param name="room">The room to copy to.</param>
		/// <remarks>Doesn't copy IDs, cache type, contained entities, or exits.</remarks>
		public void CopyTo(Room room)
		{
			Guard.ThrowIfNull(room, nameof(room));

			room.Name = string.Copy(Name);
			room.Description = string.Copy(Description);
			room.Tier.Level = Tier.Level;
		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		override protected void OnObjectDestroyed(object source, CacheObjectRemovedEventArgs args)
		{
			// Safely remove players without deleting/disconnecting them
			var players = GetAllEntities<Player>();

			foreach (var player in players)
				_entityList.Remove(player);

			DataAccess.RemoveMany(_entityList, args.CacheType);
		}
	}
}