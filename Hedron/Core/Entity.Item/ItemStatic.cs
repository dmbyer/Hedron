using Hedron.Core.Behavior;
using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Entity.Property;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;

namespace Hedron.Core.Entity.Item
{
	public class ItemStatic : EntityInanimate
	{
		/// <summary>
		/// Guarantees an ItemStatic slot will always be None
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
		public ItemStatic() : base()
		{
			Behavior = new ItemBehavior();
			Slot = ItemSlot.None;
		}

		/// <summary>
		/// Creates a new item and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype item</returns>
		public static ItemStatic NewPrototype(uint? prototypeID = null)
		{
			var newItem = new ItemStatic();

			DataAccess.Add<ItemStatic>(newItem, CacheType.Prototype, prototypeID);

			return newItem;
		}

		/// <summary>
		/// Creates a new item and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced item</returns>
		public static ItemStatic NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<ItemStatic>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<ItemStatic>(DataAccess.Add<ItemStatic>(new ItemStatic(), CacheType.Instance), CacheType.Instance);
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

			Logger.Info(nameof(ItemStatic), nameof(Spawn), "Spawning item: " + Name + ": ProtoID=" + Prototype.ToString());

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

			Logger.Info(nameof(ItemStatic), nameof(Spawn), "Finished spawning item.");

			return newItem.Instance;
		}
	}
}