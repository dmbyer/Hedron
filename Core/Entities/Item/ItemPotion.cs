using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Data;
using Hedron.Core.System;
using Newtonsoft.Json;

namespace Hedron.Core.Entities.Item
{
	public class ItemPotion : EntityInanimate
	{
		/// <summary>
		/// The restorative properties of the potion
		/// </summary>
		[JsonProperty]
		public Pools PoolRestoration { get; set; } = new Pools();

		/// <summary>
		/// The Effect the potion should provide
		/// </summary>
		[JsonProperty]
		public Effect Effect { get; set; }

		/// <summary>
		/// Guarantees an ItemPotion slot will always be None
		/// </summary>
		public override ItemSlot Slot
		{
			get
			{
				return _slot;
			}
			set
			{
				_slot = ItemSlot.None;
			}
		}

		// Constructors
		public ItemPotion() : base()
		{
			Behavior.Obtainable = true;
			Behavior.Storable = true;
			Slot = ItemSlot.None;
		}

		/// <summary>
		/// Creates a new potion and adds it to the Prototype cache
		/// </summary>
		/// <returns>The new prototype item</returns>
		public static ItemPotion NewPrototype()
		{
			var newItem = new ItemPotion();
			DataAccess.Add<ItemPotion>(newItem, CacheType.Prototype);
			return newItem;
		}

		/// <summary>
		/// Creates a new potion and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype.</param>
		/// <returns>The new instanced item</returns>
		public static ItemPotion NewInstance(bool withPrototype)
		{
			ItemPotion newItem = new ItemPotion();

			if (withPrototype)
			{
				var newProto = NewPrototype();
				newItem.Prototype = newProto.Prototype;
			}

			DataAccess.Add<ItemPotion>(newItem, CacheType.Instance);

			return newItem;
		}

		/// <summary>
		/// Spawns an instance of the item from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent container.</param>
		/// <returns>The spawned item. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the item from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent container.</param>
		/// <returns>The ID of the spawned item. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override uint? Spawn(bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(ItemPotion), nameof(Spawn), "Spawning item: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance item
			var newItem = NewInstance(false);

			// Retrieve parent container and add entity
			DataAccess.Get<EntityContainer>(parent, CacheType.Instance)?.AddEntity(newItem.Instance, newItem, false);

			// Copy remaining properties
			newItem.Prototype = Prototype;
			CopyTo(newItem);

			Logger.Info(nameof(ItemPotion), nameof(Spawn), "Finished spawning potion.");

			return newItem.Instance;
		}

		/// <summary>
		/// Copies this potion's properties to another potion.
		/// </summary>
		/// <param name="item">The potion to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(ItemPotion item)
		{
			if (item == null)
				return;

			base.CopyTo(item);

			if (PoolRestoration != null)
				PoolRestoration.CopyTo(item.PoolRestoration);
			else
				item.PoolRestoration = null;

			if (item.Effect != null)
				item.Effect = Effect;
			else
				item.Effect = null;
		}
	}
}