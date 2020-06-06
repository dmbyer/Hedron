using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Item;
using Hedron.Data;
using Hedron.System;
using Newtonsoft.Json;

namespace Hedron.Core.Container
{
	/// <summary>
	/// The Inventory class contains EntityInanimates. It may be used for inventories, equipment lists, general containers, etc.
	/// </summary>
	public class Inventory : EntityContainer, ISpawnableObject
	{
		[JsonIgnore]
		public int Count
		{
			get
			{
				return _entityList.Count;
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public Inventory()
		{

		}

		/// <summary>
		/// Spawns an instance of the inventory from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The spawned inventory. Will return null if the method is called from an instanced object.</returns>
		public T SpawnAsObject<T>(bool withEntities, uint? parent = null) where T: CacheableObject
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the object from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">IGNORED: The ID of the parent.</param>
		/// <returns>The instance ID of the spawned inventory. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent is ignored because it cannot be determined which parent property needs to be set. Inventory must therefore be manually set
		/// after spawning.</remarks>
		public uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(Inventory), nameof(Spawn), "Spawning inventory: " + ": ProtoID=" + Prototype.ToString());

			var newInventory = new Inventory
			{
				Prototype = Prototype
			};

			DataAccess.Add<ItemStatic>(newInventory, CacheType.Instance);

			if (withEntities)
			{
				var entitiesToLoad = DataAccess.GetMany<EntityInanimate>(_entityList, CacheType);
				foreach (var entity in entitiesToLoad)
					entity.Spawn(withEntities, newInventory.Instance);
			}

			Logger.Info(nameof(Inventory), nameof(Spawn), "Finished spawning inventory.");

			return newInventory.Instance;
		}
	}
}