using System;
using System.Collections.Generic;
using System.Text;
using Hedron.Data;
using Hedron.Core.Damage;
using Hedron.System;
using Newtonsoft.Json;
using Hedron.Core.Behavior;
using Hedron.Core.Property;

namespace Hedron.Core
{
	public class ItemWeapon : EntityInanimate
	{
		/// <summary>
		/// Damage properties
		/// </summary>
		[JsonProperty]
		public DamageProperties DamageType { get; set; }

		/// <summary>
		/// Min weapon damage
		/// </summary>
		[JsonProperty]
		public int              MinDamage  { get; set; } = Constants.DEFAULT_DAMAGE;

		/// <summary>
		/// Max weapon damage
		/// </summary>
		[JsonProperty]
		public int              MaxDamage  { get; set; } = Constants.DEFAULT_DAMAGE * 2;

		/// <summary>
		/// Damage properties
		/// </summary>
		[JsonProperty]
		public Affect           Affects    { get; set; } = new Affect();

		/// <summary>
		/// Base constructor
		/// </summary>
		private ItemWeapon() : base()
		{
			Behavior = new ItemBehavior()
			{
				Obtainable = true,
				Storable = true,
				RandomDrop = true
			};
		}
		
		/// <summary>
		/// Constructs a new weapon
		/// </summary>
		/// <param name="slot">The weapon slot</param>
		/// <remarks>This constructor ensures a weapon will be created as either one or two handed</remarks>
		public ItemWeapon(ItemSlot slot = ItemSlot.OneHandedWeapon) : this()
		{
			if (slot != ItemSlot.OneHandedWeapon && slot != ItemSlot.TwoHandedWeapon)
				Slot = ItemSlot.OneHandedWeapon;
			else
				Slot = slot;
		}

		/// <summary>
		/// Creates a new weapon and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <returns>The new prototype weapon</returns>
		public static ItemWeapon NewPrototype(uint? prototypeID = null)
		{
			var newWeapon = new ItemWeapon();

			DataAccess.Add<ItemWeapon>(newWeapon, CacheType.Prototype, prototypeID);

			return newWeapon;
		}

		/// <summary>
		/// Creates a new weapon and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced weapon</returns>
		public static ItemWeapon NewInstance(bool withPrototype = false, uint? prototypeID = null)
		{
			return withPrototype
				? DataAccess.Get<ItemWeapon>(NewPrototype(prototypeID).Spawn(false), CacheType.Instance)
				: DataAccess.Get<ItemWeapon>(DataAccess.Add<ItemWeapon>(new ItemWeapon(), CacheType.Instance), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the weapon from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The spawned weapon. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint? parent = null)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the weapon from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent.</param>
		/// <returns>The ID of the spawned weapon. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override uint? Spawn(bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(ItemWeapon), nameof(Spawn), "Spawning weapon: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance item
			var newWeapon = NewInstance(false);

			// Retrieve parent container and add entity
			var parentContainer = DataAccess.Get<ICacheableObject>(parent, CacheType.Instance);

			if (parentContainer?.GetType() == typeof(Room))
				((Room)parentContainer).AddEntity(newWeapon.Instance, newWeapon);

			if (parentContainer?.GetType() == typeof(Inventory))
				((Inventory)parentContainer).AddEntity(newWeapon.Instance, newWeapon);

			// Copy remaining properties
			newWeapon.Prototype = Prototype;
			CopyTo(newWeapon);

			Logger.Info(nameof(ItemWeapon), nameof(Spawn), "Finished spawning weapon.");

			return newWeapon.Instance;
		}
	}
}
