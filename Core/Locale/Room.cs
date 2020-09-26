using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Factory;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Locale;
using Hedron.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Locale
{

	public class Room : CacheableObject, ICopyableObject<Room>, IEntityChildOfArea
	{
		/// <summary>
		/// The room's living entities
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		public EntityContainer Animates { get; protected set; } = new EntityContainer();

		/// <summary>
		/// The room's objects
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		public EntityContainer Items { get; protected set; } = new EntityContainer();

		/// <summary>
		/// The room's shop items
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		public EntityContainer ShopItems { get; protected set; } = new EntityContainer();

		/// <summary>
		/// The room's storage items
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		public EntityContainer StorageItems { get; protected set; } = new EntityContainer();

		[JsonProperty]
		public RoomExits Exits { get; private set; } = new RoomExits();

		[JsonProperty]
		public Tier Tier { get; private set; } = new Tier();

		[JsonProperty]
		public string Name { get; set; }

		[JsonProperty]
		public string Description { get; set; }

		[JsonProperty]
		public bool IsShop { get; set; }

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
		/// <param name="parentID">The parent ID of the area to add this room to.</param>
		/// <returns>The new prototype room</returns>
		public static Room NewPrototype(uint parentID)
		{
			var newRoom = new Room();
			newRoom.Animates.CacheType = CacheType.Prototype;
			newRoom.Items.CacheType = CacheType.Prototype;
			newRoom.ShopItems.CacheType = CacheType.Prototype;
			newRoom.StorageItems.CacheType = CacheType.Prototype;

			var area = DataAccess.Get<Area>(parentID, CacheType.Prototype);

			if (area == null)
				throw new LocaleException($"Failed to locate parent ID ({parentID}) when generating new room protoype.");

			DataAccess.Add<Room>(newRoom, CacheType.Prototype);

			newRoom.Animates.PrototypeParents.Add((uint)newRoom.Prototype);
			newRoom.Items.PrototypeParents.Add((uint)newRoom.Prototype);
			newRoom.ShopItems.PrototypeParents.Add((uint)newRoom.Prototype);
			newRoom.StorageItems.PrototypeParents.Add((uint)newRoom.Prototype);

			DataAccess.Add<EntityContainer>(newRoom.Animates, CacheType.Prototype);
			DataAccess.Add<EntityContainer>(newRoom.Items, CacheType.Prototype);
			DataAccess.Add<EntityContainer>(newRoom.ShopItems, CacheType.Prototype);
			DataAccess.Add<EntityContainer>(newRoom.StorageItems, CacheType.Prototype);

			area.Rooms.AddEntity(newRoom.Prototype, newRoom, true);

			DataPersistence.SaveObject(newRoom);
			return newRoom;
		}

		/// <summary>
		/// Creates a new room and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype. If so, the Prototype Area will also have the
		/// prototype room added.</param>
		/// <param name="parentID">The parent area Instance ID.</param>
		/// <returns>The new instanced room</returns>
		public static Room NewInstance(bool withPrototype, uint parentID)
		{
			Room newRoom;

			if (withPrototype)
			{
				var instanceArea = DataAccess.Get<Area>(parentID, CacheType.Instance);
				if (instanceArea == null)
					throw new LocaleException($"{nameof(Room)}.{nameof(NewInstance)}: parentID retrieved null instance area.");

				var protoArea = DataAccess.Get<Area>(instanceArea.Prototype, CacheType.Prototype);
				if (protoArea == null)
					throw new LocaleException($"{nameof(Room)}.{nameof(NewInstance)}: parentID retrieved null prototype area.");

				newRoom = DataAccess.Get<Room>(NewPrototype((uint)protoArea.Prototype).Spawn(false, parentID), CacheType.Instance);
			}
			else
			{
				var area = DataAccess.Get<Area>(parentID, CacheType.Instance);
				if (area == null)
					throw new LocaleException($"{nameof(Room)}.{nameof(NewInstance)}: parentID retrieved null instance area.");

				newRoom = DataAccess.Get<Room>(DataAccess.Add<Room>(new Room(), CacheType.Instance), CacheType.Instance);
				DataAccess.Add<EntityContainer>(newRoom.Animates, CacheType.Instance);
				DataAccess.Add<EntityContainer>(newRoom.Items, CacheType.Instance);
				DataAccess.Add<EntityContainer>(newRoom.ShopItems, CacheType.Instance);
				DataAccess.Add<EntityContainer>(newRoom.StorageItems, CacheType.Instance);

				area.Rooms.AddEntity(newRoom.Instance, newRoom, false);
			}

			newRoom.Animates.InstanceParent = newRoom.Instance;
			newRoom.Items.InstanceParent = newRoom.Instance;
			newRoom.ShopItems.InstanceParent = newRoom.Instance;
			newRoom.StorageItems.InstanceParent = newRoom.Instance;

			return newRoom;
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

			// Update persistence since data structure has changed
			if (CacheType == CacheType.Prototype)
				DataPersistence.SaveObject(this);
		}

		public Area GetInstanceParentArea()
		{
			if (CacheType != CacheType.Instance)
				return null;

			var parentContainer = DataAccess.Get<EntityContainer>(InstanceParent, CacheType.Instance);
			return DataAccess.Get<Area>(parentContainer?.InstanceParent, CacheType.Instance);
		}

		/// <summary>
		/// Retrieves a combined list of all entities in this room excluding shop items
		/// </summary>
		/// <returns>A list of entities</returns>
		public List<T> GetAllEntities<T>() where T: IEntity
		{
			var animates = Animates.GetAllEntities<EntityAnimate>().Cast<IEntity>();
			var items = Items.GetAllEntities<EntityInanimate>().Cast<IEntity>();

			return animates.Concat(items).Where(i => i.GetType() == typeof(T)).Cast<T>().ToList();
		}

		public List<Area> GetPrototypeParentAreas()
		{
			var areas = new List<Area>();
			var proto = DataAccess.Get<Area>(Prototype, CacheType.Prototype);

			var parentContainers = DataAccess.GetMany<EntityContainer>(proto?.PrototypeParents, CacheType.Prototype);
			return DataAccess.GetMany<Area>(parentContainers.Select(a => a.Prototype).Cast<uint>().ToList(), CacheType.Prototype);
		}

		/// <summary>
		/// Spawns an instance of the room from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent area instance ID</param>
		/// <returns>The spawned room. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits will all be null and must be fixed from prototype. Parent cannot be null. Adds new room to instanced area.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
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
		public override uint? Spawn(bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			var parentContainer = DataAccess.Get<EntityContainer>(parent, CacheType.Instance);
			var parentArea = DataAccess.Get<Area>(parentContainer.InstanceParent, CacheType.Instance);

			if (parentContainer == null || parentArea == null)
				throw new LocaleException("Parent cannot be null when spawning a room.");

			Logger.Info(nameof(Room), nameof(Spawn), "Spawning room: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance room and add to parent area
			var newRoom = DataAccess.Get<Room>(DataAccess.Add<Room>(new Room(), CacheType.Instance), CacheType.Instance);
			parentArea.Rooms.AddEntity(newRoom.Instance, newRoom, false);

			// Set remaining properties
			newRoom.Prototype = Prototype;
			CopyTo(newRoom);

			// Spawn contained entities
			if (withEntities)
			{
				newRoom.Animates = Animates.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				foreach (var animate in Animates.GetAllEntitiesAsObjects<EntityAnimate>())
					animate.Spawn(withEntities, (uint)newRoom.Animates.Instance);

				newRoom.Items = Items.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				foreach (var item in Items.GetAllEntitiesAsObjects<EntityInanimate>())
					item.Spawn(withEntities, (uint)newRoom.Items.Instance);

				newRoom.ShopItems = ShopItems.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				foreach (var item in ShopItems.GetAllEntitiesAsObjects<EntityInanimate>())
					item.Spawn(withEntities, (uint)newRoom.ShopItems.Instance);

				newRoom.StorageItems = StorageItems.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				foreach (var item in StorageItems.GetAllEntitiesAsObjects<EntityInanimate>())
					item.Spawn(withEntities, (uint)newRoom.StorageItems.Instance);
			}
			else
			{
				newRoom.Animates = Animates.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				newRoom.Items = Items.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				newRoom.ShopItems = ShopItems.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
				newRoom.StorageItems = StorageItems.SpawnAsObject<EntityContainer>(!withEntities, (uint)newRoom.Instance);
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

			room.Name = Name;
			room.Description = Description;
			room.Tier.Level = Tier.Level;
			room.IsShop = IsShop;
		}

		/// <summary>
		/// Handles the ObjectDestroyed event. Only DataAccess should call this directly.
		/// </summary>
		/// <param name="source">The object raising the event</param>
		/// <param name="args">The generic event args</param>
		override protected void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{
			// Safely remove players without deleting/disconnecting them
			var players = Animates.GetAllEntitiesAsObjects<Player>();

			foreach (var player in players)
				Animates.RemoveEntity(player?.Instance, player);

			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? Animates.Instance : Animates.Prototype, args.CacheType);
			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? Items.Instance : Items.Prototype, args.CacheType);
			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? ShopItems.Instance : ShopItems.Prototype, args.CacheType);
			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? StorageItems.Instance : StorageItems.Prototype, args.CacheType);
		}
	}
}