using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Container;
using Hedron.Core.Entity;
using Hedron.System;
using Hedron.Data;
using Newtonsoft.Json;

namespace Hedron.Core
{
    public class World : EntityContainer, ICopyableObject<World>, ISpawnableObject
	{
		[JsonIgnore]
		public bool        IsLoading        { get; private set; }

		[JsonIgnore]
		public static bool Shutdown         { get; set; }

        public string      Name             { get; set; }
		public uint?       StartingLocation { get; set; } = null;

		/// <summary>
		/// Default constructor
		/// </summary>
		public World() : base()
		{
			Name = Constants.DEFAULT_WORLD_NAME;
		}

		/// <summary>
		/// Creates a new world and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype world</returns>
		public static World NewPrototype(uint? prototypeID = null)
		{
			var newWorld = new World();

			DataAccess.Add<Area>(newWorld, CacheType.Prototype, prototypeID);

			return newWorld;
		}

		/// <summary>
		/// Creates a new world and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced world</returns>
		public static World NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<World>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<World>(DataAccess.Add<World>(new World(), CacheType.Instance), CacheType.Instance);
		}

		/// <summary>
		/// Saves the world to the filesystem
		/// </summary>
		/// <param name="path">The folder path to save to. Uses default location if unspecified.</param>
        public void SaveWorld()
        {
			// Write world
			DataPersistence.SaveObject(this);

			// Write areas
			foreach (var area in DataAccess.GetAll<Area>(CacheType.Prototype))
				DataPersistence.SaveObject(area);

			// Write rooms
			foreach (var room in DataAccess.GetAll<Room>(CacheType.Prototype))
				DataPersistence.SaveObject(room);

			// Write mobs
			foreach (var mob in DataAccess.GetAll<Mob>(CacheType.Prototype))
				DataPersistence.SaveObject(mob);

			// Write inventories
			foreach (var inventory in DataAccess.GetAll<Inventory>(CacheType.Prototype))
				DataPersistence.SaveObject(inventory);

			// Write static items
			foreach (var istatic in DataAccess.GetAll<ItemStatic>(CacheType.Prototype))
				DataPersistence.SaveObject(istatic);

			// Write weapons
			foreach (var iweapon in DataAccess.GetAll<ItemWeapon>(CacheType.Prototype))
				DataPersistence.SaveObject(iweapon);
		}

		/// <summary>
		/// Loads a new prototype world.
		/// </summary>
		/// <param name="instantiate">Whether to also instantiate all loaded prototypes</param>
		/// <param name="basePath">The location of the world to load</param>
		/// <remarks>If this is called on a world that has already been loaded, it will be wiped from the cache and reloaded.</remarks>
		public static World LoadWorld(bool instantiate, string basePath = null)
		{
			World newWorld = new World
			{
				IsLoading = true,
				CacheType = CacheType.Prototype
			};

			// Clear the current cache
			DataAccess.WipeCache();

			// Provision data container to load items into rooms once all data has been loaded
			Dictionary<uint?, List<uint>> entitiesToLoadInRooms = new Dictionary<uint?, List<uint>>();

			// Provision data container to load inventory items into mobs once all data has been loaded
			Dictionary<uint?, List<uint>> itemsToLoadInInventory = new Dictionary<uint?, List<uint>>();

			// Set read path
			if (basePath == "" || basePath == null)
				basePath = DataPersistence.PersistencePath;

			if (!basePath.EndsWith(@"\"))
				basePath += @"\";

			// Initialize collections for adding entities to their parents post-load
			// Intentionally allow throwing an exception if the path doesn't exist
			var world = Directory.GetFiles(basePath + typeof(World).ToString(), @"*.json");

			var areas = new string[0];
			var rooms = new string[0];
			var inventories = new string[0];
			var mobs = new string[0];
			var itemStatics = new string[0];
			var itemWeapons = new string[0];

			if (Directory.Exists(basePath + typeof(Area).ToString()))
				areas = Directory.GetFiles(basePath + typeof(Area).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(Room).ToString()))
				rooms = Directory.GetFiles(basePath + typeof(Room).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(Inventory).ToString()))
				inventories = Directory.GetFiles(basePath + typeof(Inventory).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(Mob).ToString()))
				mobs = Directory.GetFiles(basePath + typeof(Mob).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(ItemStatic).ToString()))
				itemStatics = Directory.GetFiles(basePath + typeof(ItemStatic).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(ItemWeapon).ToString()))
				itemWeapons = Directory.GetFiles(basePath + typeof(ItemWeapon).ToString(), @"*.json");

			// Load world
			if (world.Length > 0)
			{
				DataPersistence.LoadObject(world[0], out newWorld);

				// Areas will be repopulated via AddArea method
				newWorld.RemoveAllEntities(false);

				// Add to cache
				DataAccess.Add<World>(newWorld, CacheType.Prototype, newWorld.Prototype, false);
			}
			else
			{
				// No <worldname>.json found
				Logger.Error(nameof(World), 
					nameof(LoadWorld), 
					string.Format(@"No .json file found to load in {0}\{1}", basePath, typeof(World).ToString()));
				return default(World);
			}

			// Load rooms
			foreach (var room in rooms)
			{
				// Add room prototype
				var newRoom = new Room();
				DataPersistence.LoadObject(room, out newRoom);

				// Populate room->entity map for post-loading
				entitiesToLoadInRooms.Add(newRoom.Prototype, newRoom.GetAllEntities());

				// Clear all room IDs so they can be added post-load
				newRoom.RemoveAllEntities(false);

				// Add to cache
				DataAccess.Add<Room>(newRoom, CacheType.Prototype, newRoom.Prototype, false);
			}

			// Load areas
			foreach (var area in areas)
			{
				// Add area prototype
				var newArea = new Area();
				DataPersistence.LoadObject(area, out newArea);

				// Add to cache
				DataAccess.Add<Area>(newArea, CacheType.Prototype, newArea.Prototype, false);

				// Create temporary list of all rooms to add to area
				var roomsToAdd = newArea.GetAllEntities<Room>();

				// Clear all room IDs so they can be added and attached to event handling
				newArea.RemoveAllEntities(false);

				// Add area to world
				newWorld.AddEntity(newArea.Prototype, newArea, false);

				// Add rooms to area
				foreach (var room in roomsToAdd)
				{
					var addRoom = DataAccess.Get<Room>(room, CacheType.Prototype);
					newArea.AddEntity(addRoom.Prototype, addRoom, false);
				}
			}

			// Load inventories
			foreach (var inventory in inventories)
			{
				// Add inventory prototype
				var newInventory = new Inventory();
				DataPersistence.LoadObject(inventory, out newInventory);

				// Populate items to load in inventories
				itemsToLoadInInventory.Add(newInventory.Prototype, newInventory.GetAllEntities());

				// Clear all item IDs so they can be added post-load
				newInventory.RemoveAllEntities(false);

				// Add to cache
				DataAccess.Add<Inventory>(newInventory, CacheType.Prototype, newInventory.Prototype, false);
			}

			// Load mobs
			foreach (var mob in mobs)
			{
				var newMob = new Mob();
				DataPersistence.LoadObject(mob, out newMob);

				// Add to cache
				DataAccess.Add<Mob>(newMob, CacheType.Prototype, newMob.Prototype, false);
			}

			// Load items
			foreach (var item in itemStatics)
			{
				var newItem = new ItemStatic();
				DataPersistence.LoadObject(item, out newItem);

				// Add to cache
				DataAccess.Add<ItemStatic>(newItem, CacheType.Prototype, newItem.Prototype, false);
			}

			// Load weapons
			foreach (var weapon in itemWeapons)
			{
				var newWeapon = new ItemWeapon();
				DataPersistence.LoadObject(weapon, out newWeapon);

				// Add to cache
				DataAccess.Add<ItemWeapon>(newWeapon, CacheType.Prototype, newWeapon.Prototype, false);
			}

			// Add entities to rooms
			foreach (var kvp in entitiesToLoadInRooms)
			{
				var room = DataAccess.Get<Room>(kvp.Key, CacheType.Prototype);
				foreach (var item in kvp.Value)
				{
					var i = DataAccess.Get<EntityBase>(item, CacheType.Prototype);
					room.AddEntity(item, i, false);
				}
			}

			// Add items to inventories
			foreach (var kvp in itemsToLoadInInventory)
			{
				var inventory = DataAccess.Get<Inventory>(kvp.Key, CacheType.Prototype);
				foreach (var item in kvp.Value)
				{
					var i = DataAccess.Get<EntityInanimate>(item, CacheType.Prototype);
					inventory.AddEntity(item, i, false);
				}
			}

			// Fix prototype null exits
			RoomExits.FixNullExits(CacheType.Prototype);

			if (instantiate)
			{
				Logger.Info(nameof(World), nameof(LoadWorld), "Instantiating world.");
				newWorld.Spawn(instantiate);

				// Fix room links
				Logger.Info(nameof(World), nameof(LoadWorld), "Linking instanced room exits.");
				RoomExits.LinkInstancedRoomExits();

				// Final pass at fixing null exits for instanced rooms
				Logger.Info(nameof(World), nameof(LoadWorld), "Fixing null exits.");
				RoomExits.FixNullExits(CacheType.Instance);
				
				Logger.Info(nameof(World), nameof(LoadWorld), "Finished instantiating world.");
			}

			newWorld.IsLoading = false;

			return newWorld;
		}

		/// <summary>
		/// Spawns an instance of the world from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained areas</param>
		/// <param name="parent">The parent instance ID; ignored</param>
		/// <returns>The spawned world. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits of rools will all be null and must be fixed from prototype.</remarks>
		public T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T : CacheableObject
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the world from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained areas.</param>
		/// <param name="parent">The parent instance ID; ignored</param>
		/// <returns>The instance ID of the spawned world. Will return null if the method is called from an instanced object.</returns>
		public uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(World), nameof(Spawn), "Spawning world: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance world
			var newWorld = NewInstance(false);

			// Set remaining properties
			newWorld.Prototype = Prototype;
			// newWorld.Parent = null;
			CopyTo(newWorld);

			// Spawn contained rooms
			if (withEntities)
			{
				foreach (var area in DataAccess.GetMany<Area>(_entityList, CacheType.Prototype))
					area.Spawn(withEntities, newWorld.Instance);
			}

			RoomExits.LinkInstancedRoomExits();
			RoomExits.FixNullExits(CacheType.Instance);

			Logger.Info(nameof(World), nameof(Spawn), "Finished spawning world.");

			return newWorld.Instance;
		}

		/// <summary>
		/// Copies this world's properties to another world.
		/// </summary>
		/// <param name="world">The world to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public void CopyTo(World world)
		{
			Guard.ThrowIfNull(world, nameof(world));

			world.Name = string.Copy(Name);
			world.StartingLocation = StartingLocation;
		}
	}
}