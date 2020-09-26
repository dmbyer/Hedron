using Hedron.Core.Container;
using Hedron.Core.Damage;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Hedron.Core.Entities.Item
{
	public class ItemWeapon : EntityInanimate
	{
		/// <summary>
		/// Guarantees an ItemWeapon slot will always be OneHandedWeapon if set to anything other than a weapon slot
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
		public override ItemSlot Slot
		{
			get
			{
				return _slot;
			}
			set
			{
				if (value != ItemSlot.OneHandedWeapon && value != ItemSlot.TwoHandedWeapon)
					_slot = ItemSlot.OneHandedWeapon;
				else
					_slot = value;
			}
		}

		/// <summary>
		/// Damage type
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
		public DamageType DamageType { get; set; }

		/// <summary>
		/// Minimum weapon damage
		/// </summary>
		[JsonProperty]
		public int MinDamage  { get; set; } = Constants.DEFAULT_DAMAGE;

		/// <summary>
		/// Maximum weapon damage
		/// </summary>
		[JsonProperty]
		public int MaxDamage  { get; set; } = Constants.DEFAULT_DAMAGE * 2;

		/// <summary>
		/// The type of weapon
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(StringEnumConverter))]
		public WeaponType WeaponType { get; set; }

		/// <summary>
		/// Base constructor
		/// </summary>
		public ItemWeapon() : base()
		{
			Behavior = new ItemBehavior()
			{
				Obtainable = true,
				Storable = true,
				RandomDrop = true
			};

			Slot = ItemSlot.OneHandedWeapon;
		}
		
		/// <summary>
		/// Constructs a new weapon
		/// </summary>
		/// <param name="slot">The weapon slot</param>
		/// <remarks>This constructor ensures a weapon will be created as either one or two handed</remarks>
		public ItemWeapon(ItemSlot slot) : this()
		{
			if (slot != ItemSlot.OneHandedWeapon && slot != ItemSlot.TwoHandedWeapon)
				Slot = ItemSlot.OneHandedWeapon;
			else
				Slot = slot;
		}

		/// <summary>
		/// Creates a new static item and adds it to the Prototype cache
		/// </summary>
		/// <returns>The new prototype item</returns>
		public static ItemWeapon NewPrototype()
		{
			var newItem = new ItemWeapon();
			DataAccess.Add<ItemWeapon>(newItem, CacheType.Prototype);
			return newItem;
		}

		/// <summary>
		/// Creates a new weapon and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype.</param>
		/// <returns>The new instanced item</returns>
		public static ItemWeapon NewInstance(bool withPrototype)
		{
			ItemWeapon newItem = new ItemWeapon();

			if (withPrototype)
			{
				var newProto = NewPrototype();
				newItem.Prototype = newProto.Prototype;
			}

			DataAccess.Add<ItemWeapon>(newItem, CacheType.Instance);

			return newItem;
		}

		/// <summary>
		/// Spawns an instance of the weapon from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent container.</param>
		/// <returns>The spawned weapon. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
		{
			return DataAccess.Get<T>(Spawn(withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the weapon from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities.</param>
		/// <param name="parent">The ID the the parent container.</param>
		/// <returns>The ID of the spawned weapon. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new item to parent, if specified.</remarks>
		public override uint? Spawn(bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(ItemWeapon), nameof(Spawn), "Spawning weapon: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance item
			var newWeapon = NewInstance(false);

			// Retrieve parent container and add entity
			DataAccess.Get<EntityContainer>(parent, CacheType.Instance).AddEntity(newWeapon.Instance, newWeapon, false);

			// Copy remaining properties
			newWeapon.Prototype = Prototype;
			newWeapon.DamageType = DamageType;
			CopyTo(newWeapon);

			Logger.Info(nameof(ItemWeapon), nameof(Spawn), "Finished spawning weapon.");

			return newWeapon.Instance;
		}

		/// <summary>
		/// Copies this weapon's properties to another weapon.
		/// </summary>
		/// <param name="item">The weapon to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(ItemWeapon item)
		{
			if (item == null)
				return;

			base.CopyTo(item);

			item.DamageType = DamageType;
			item.MinDamage = MinDamage;
			item.MaxDamage = MaxDamage;
			item.WeaponType = WeaponType;
		}
	}
}