using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Item;
using Hedron.Core.Entities.Living;
using Hedron.Data;
using Hedron.Core.Factory;
using Hedron.Core.System;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hedron.Core.Locale
{
	public class World : CacheableObject, ICopyableObject<World>
	{
		private event EventHandler<EventArgs> WorldLoaded;

		/// <summary>
		/// Provides a global random object to use
		/// </summary>
		/// <remarks>This is useful to avoid rapidly executing loops generating a new random object from having the same seed.</remarks>
		[JsonIgnore]
		public static Random Random = new Random();

		[JsonIgnore]
		public bool IsLoading { get; protected set; }

		[JsonIgnore]
		public static bool Shutdown { get; set; }

		public string Name { get; set; }

		/// <summary>
		/// The room ID for new players entering the world
		/// </summary>
		public uint? StartingLocation { get; set; } = null;

		/// <summary>
		/// The area list for the world
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		public EntityContainer Areas { get; protected set; } = new EntityContainer();

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
		/// <returns>The new prototype world</returns>
		public static World NewPrototype()
		{
			var newWorld = new World();
			newWorld.Areas.CacheType = CacheType.Prototype;

			DataAccess.Add<World>(newWorld, CacheType.Prototype);
			newWorld.Areas.PrototypeParents.Add((uint)newWorld.Prototype);
			DataAccess.Add<EntityContainer>(newWorld.Areas, CacheType.Prototype);

			DataPersistence.SaveObject(newWorld);
			return newWorld;
		}

		/// <summary>
		/// Creates a new world and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <returns>The new instanced world</returns>
		public static World NewInstance(bool withPrototype)
		{
			World newWorld;

			if (withPrototype)
			{
				newWorld = DataAccess.Get<World>(NewPrototype().Spawn(false, 0), CacheType.Instance);
			}
			else
			{
				newWorld = DataAccess.Get<World>(DataAccess.Add<World>(new World(), CacheType.Instance), CacheType.Instance);
				DataAccess.Add<EntityContainer>(newWorld.Areas, CacheType.Instance);
			}

			newWorld.Areas.InstanceParent = newWorld.Instance;
			return newWorld;
		}

		/// <summary>
		/// Loads a new prototype world.
		/// </summary>
		/// <param name="instantiate">Whether to also instantiate all loaded prototypes</param>
		/// <param name="basePath">The location of the world to load</param>
		/// <remarks>If this is called on a world that has already been loaded, it will be wiped from the cache and reloaded.</remarks>
		public World LoadWorld(bool instantiate, string basePath = null)
		{
			// Remove the current world from the prototype cache in case it is being reloaded
			DataAccess.Remove<World>(Prototype, CacheType.Prototype);

			World newWorld = new World
			{
				IsLoading = true,
				CacheType = CacheType.Prototype
			};

			newWorld.Areas.CacheType = CacheType.Prototype;

			// Set read path
			if (basePath == "" || basePath == null)
				basePath = DataPersistence.PersistencePath;

			if (!basePath.EndsWith(@"\"))
				basePath += @"\";

			string[] world;

			try
			{
				world = Directory.GetFiles(basePath + typeof(World).ToString(), @"*.json");
			}
			catch
			{
				// No world was found in the filesystem
				newWorld = NewPrototype();
				newWorld.IsLoading = true;

				world = Directory.GetFiles(basePath + typeof(World).ToString(), @"*.json");
			}

			var areas = new string[0];
			var rooms = new string[0];
			var containers = new string[0];
			var mobs = new string[0];
			var potions = new string[0];
			var statics = new string[0];
			var weapons = new string[0];

			if (Directory.Exists(basePath + typeof(Area).ToString()))
				areas = Directory.GetFiles(basePath + typeof(Area).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(Room).ToString()))
				rooms = Directory.GetFiles(basePath + typeof(Room).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(EntityContainer).ToString()))
				containers = Directory.GetFiles(basePath + typeof(EntityContainer).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(Mob).ToString()))
				mobs = Directory.GetFiles(basePath + typeof(Mob).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(ItemPotion).ToString()))
				potions = Directory.GetFiles(basePath + typeof(ItemPotion).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(ItemStatic).ToString()))
				statics = Directory.GetFiles(basePath + typeof(ItemStatic).ToString(), @"*.json");

			if (Directory.Exists(basePath + typeof(ItemWeapon).ToString()))
				weapons = Directory.GetFiles(basePath + typeof(ItemWeapon).ToString(), @"*.json");

			// Load containers
			foreach (var container in containers)
			{
				// Add container prototype
				var newContainer = new EntityContainer();
				DataPersistence.LoadObject(container, out newContainer);

				newWorld.WorldLoaded += newContainer.World_HandleLoaded;

				// Add to cache
				try
				{
					DataAccess.Add<EntityContainer>(newContainer, CacheType.Prototype, newContainer.Prototype, false);
				}
				catch (ArgumentException ex)
				{
					// The container was already added to the cache as a result of creating a new world
					continue;
				}
			}

			// Load world
			foreach (var w in world)
			{
				DataPersistence.LoadObject(w, out newWorld);
				try
				{
					DataAccess.Add<World>(newWorld, CacheType.Prototype, newWorld.Prototype, false);
				}
				catch (ArgumentException ex)
				{
					// The world was already added to the cache as a result of creating a new world
					continue;
				}
			}

			// Load rooms
			foreach (var room in rooms)
			{
				// Add room prototype
				var newRoom = new Room();
				DataPersistence.LoadObject(room, out newRoom);

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
			foreach (var item in statics)
			{
				var newItem = new ItemStatic();
				DataPersistence.LoadObject(item, out newItem);

				// Add to cache
				DataAccess.Add<ItemStatic>(newItem, CacheType.Prototype, newItem.Prototype, false);
			}

			// Load potions
			foreach (var item in potions)
			{
				var newPotion = new ItemPotion();
				DataPersistence.LoadObject(item, out newPotion);

				// Add to cache
				DataAccess.Add<ItemStatic>(newPotion, CacheType.Prototype, newPotion.Prototype, false);
			}

			// Load weapons
			foreach (var weapon in weapons)
			{
				var newWeapon = new ItemWeapon();
				DataPersistence.LoadObject(weapon, out newWeapon);

				// Add to cache
				DataAccess.Add<ItemWeapon>(newWeapon, CacheType.Prototype, newWeapon.Prototype, false);
			}

			// Add entities to containers
			// OnWorldLoaded(new EventArgs());

			// Fix prototype null exits
			RoomExits.FixNullExits(CacheType.Prototype);

			newWorld.IsLoading = false;

			if (instantiate)
			{
				Logger.Info(nameof(World), nameof(LoadWorld), "Instantiating world.");
				newWorld = newWorld.SpawnAsObject<World>(instantiate, 0);

				Logger.Info(nameof(World), nameof(LoadWorld), "Finished instantiating world.");
			}

			return newWorld;
		}
		/// <summary>
		/// Spawns an instance of the world from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained areas</param>
		/// <param name="parent">The parent instance ID; ignored</param>
		/// <returns>The spawned world. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Exits of rools will all be null and must be fixed from prototype.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the world from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained areas.</param>
		/// <param name="parent">The parent instance ID; ignored</param>
		/// <returns>The instance ID of the spawned world. Will return null if the method is called from an instanced object.</returns>
		public override uint? Spawn(bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(World), nameof(Spawn), "Spawning world: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance world
			var newWorld = DataAccess.Get<World>(DataAccess.Add<World>(new World(), CacheType.Instance), CacheType.Instance);

			// Set remaining properties
			newWorld.Prototype = Prototype;
			CopyTo(newWorld);

			// Spawn contained areas
			if (withEntities)
			{
				newWorld.Areas = Areas.SpawnAsObject<EntityContainer>(!withEntities, (uint)newWorld.Instance);
				foreach (var area in Areas.GetAllEntitiesAsObjects<Area>())
					area.Spawn(withEntities, (uint)newWorld.Areas.Instance);
			}
			else
			{
				newWorld.Areas = Areas.SpawnAsObject<EntityContainer>(!withEntities, (uint)newWorld.Instance);
			}

			newWorld.StartingLocation = DataAccess.GetInstancesOfPrototype<Room>(StartingLocation).FirstOrDefault()?.Instance;

			Logger.Info(nameof(World), nameof(LoadWorld), "Linking instanced room exits.");
			RoomExits.LinkInstancedRoomExits();

			Logger.Info(nameof(World), nameof(LoadWorld), "Fixing null exits.");
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

			world.Name = Name;
			world.StartingLocation = StartingLocation;
		}

		/// <summary>
		/// Handles the world loaded event
		/// </summary>
		/// <param name="e">The event args</param>
		protected virtual void OnWorldLoaded(EventArgs e)
		{
			// TODO: Implement true observer pattern
			WorldLoaded?.Invoke(this, new EventArgs());
			WorldLoaded = null;
		}
	}
}