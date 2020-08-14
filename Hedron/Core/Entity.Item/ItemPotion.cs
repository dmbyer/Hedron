﻿using Hedron.Core.Behavior;
using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using Newtonsoft.Json;

namespace Hedron.Core.Entity.Item
{
	public class ItemPotion : EntityInanimate
	{
		/// <summary>
		/// The restorative properties of the potion
		/// </summary>
		[JsonProperty]
		public Pools PoolRestoration { get; set; } = new Pools();

		/// <summary>
		/// The affect the potion should provide
		/// </summary>
		[JsonProperty]
		public Affect Affect { get; set; }

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
		/// Creates a new item and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype item</returns>
		public static ItemPotion NewPrototype(uint? prototypeID = null)
		{
			var newItem = new ItemPotion();

			DataAccess.Add<ItemPotion>(newItem, CacheType.Prototype, prototypeID);

			return newItem;
		}

		/// <summary>
		/// Creates a new item and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced item</returns>
		public static ItemPotion NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<ItemPotion>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<ItemPotion>(DataAccess.Add<ItemPotion>(new ItemPotion(), CacheType.Instance), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the item from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The spawned item. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint? parent = null)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the item from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The ID of the spawned item. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(ItemPotion), nameof(Spawn), "Spawning item: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance item
			var newItem = NewInstance(false);

			// Retrieve parent container and add entity
			var parentContainer = DataAccess.Get<ICacheableObject>(parent, CacheType.Instance);

			if (parentContainer?.GetType() == typeof(Room))
				((Room)parentContainer).AddEntity(newItem.Instance, newItem);

			if (parentContainer?.GetType() == typeof(Inventory))
				((Inventory)parentContainer).AddEntity(newItem.Instance, newItem);

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

			if (item.Affect != null)
				item.Affect = Affect;
			else
				item.Affect = null;
		}
	}
}