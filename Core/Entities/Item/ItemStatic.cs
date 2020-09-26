using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;

namespace Hedron.Core.Entities.Item
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
		/// Creates a new static item and adds it to the Prototype cache
		/// </summary>
		/// <returns>The new prototype item</returns>
		public static ItemStatic NewPrototype()
		{
			var newItem = new ItemStatic();
			DataAccess.Add<ItemStatic>(newItem, CacheType.Prototype);
			return newItem;
		}

		/// <summary>
		/// Creates a new static item and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype.</param>
		/// <returns>The new instanced item</returns>
		public static ItemStatic NewInstance(bool withPrototype)
		{
			ItemStatic newItem = new ItemStatic();

			if (withPrototype)
			{
				var newProto = NewPrototype();
				newItem.Prototype = newProto.Prototype;
			}

			DataAccess.Add<ItemStatic>(newItem, CacheType.Instance);

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

			Logger.Info(nameof(ItemStatic), nameof(Spawn), "Spawning item: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance item
			var newItem = NewInstance(false);

			// Retrieve parent container and add entity
			DataAccess.Get<EntityContainer>(parent, CacheType.Instance).AddEntity(newItem.Instance, newItem, false);

			// Copy remaining properties
			newItem.Prototype = Prototype;
			CopyTo(newItem);

			Logger.Info(nameof(ItemStatic), nameof(Spawn), "Finished spawning item.");

			return newItem.Instance;
		}
	}
}