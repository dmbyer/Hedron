using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Newtonsoft.Json;

namespace Hedron.Core.Entities.Living
{
	public sealed class Mob : EntityAnimate
	{
		/// <summary>
		/// The mob's behavior
		/// </summary>
		public MobBehavior Behavior { get; set; } = new MobBehavior();

		/// <summary>
		/// The affect for the mob's level
		/// </summary>
		[JsonProperty]
		private Affect LevelAffect { get; set; } = new Affect();


		private MobLevel _level;


		/// <summary>
		/// Default constructor
		/// </summary>
		public Mob() 
			: base()
		{
			LevelAffect = Affect.NewMultiplier(MobLevelModifier.Map(MobLevel.Fair));
			ModifiedPools.CopyTo(CurrentPools);
		}

		/// <summary>
		/// Creates a new mob of the given level
		/// </summary>
		/// <param name="level">The level multipler of the mob</param>
		private Mob(MobLevel level) 
			: base()
		{
			Level = level;
			LevelAffect = Affect.NewMultiplier(MobLevelModifier.Map(level));
			ModifiedPools.CopyTo(CurrentPools);
		}

		/// <summary>
		/// The mob's advancement level
		/// </summary>
		public MobLevel Level
        {
			get
            {
				return _level;
            }
			set
            {
				try
				{
					LevelAffect = Affect.NewMultiplier(MobLevelModifier.Map(value));
					_level = value;
				}
				catch
				{
					LevelAffect = Affect.NewMultiplier(MobLevelModifier.Map(MobLevel.Fair));
					_level = MobLevel.Fair;
				}
            }
        }

		/// <summary>
		/// The entity's modified attributes
		/// </summary>
		[JsonIgnore]
		public override Attributes ModifiedAttributes
		{
			get
			{
				return base.ModifiedAttributes * LevelAffect.Attributes;
			}
		}

		/// <summary>
		/// The entity's modified pools
		/// </summary>
		[JsonIgnore]
		public override Pools ModifiedPools
		{
			get
			{
				return base.ModifiedPools * LevelAffect.Pools;
			}
		}

		/// <summary>
		/// The entity's modified qualities
		/// </summary>
		[JsonIgnore]
		public override Qualities ModifiedQualities
		{
			get
			{
				return base.ModifiedQualities * LevelAffect.Qualities;
			}
		}

		/// <summary>
		/// Creates a new mob and adds it to the Prototype cache
		/// </summary>
		/// <param name="prototypeID">An optional PrototypeID. Used when loading.</param>
		/// <param name="inventoryPrototypeID">An optional PrototypeID for Inventory. Used when loading.</param>
		/// <param name="equipmentPrototypeID">An optional PrototypeID for WornEquipment. Used when loading.</param>
		/// <returns>The new prototype mob</returns>
		public static Mob NewPrototype(MobLevel level = MobLevel.Fair, uint? prototypeID = null, uint? inventoryPrototypeID = null, uint? equipmentPrototypeID = null)
		{
			var newMob = new Mob(level);

			DataAccess.Add<Mob>(newMob, CacheType.Prototype, prototypeID);
			DataAccess.Add<Inventory>(newMob.Inventory, CacheType.Prototype, inventoryPrototypeID);
			DataAccess.Add<Inventory>(newMob.WornEquipment, CacheType.Prototype, equipmentPrototypeID);

			return newMob;
		}

		/// <summary>
		/// Creates a new mob and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="prototypeID">An optional PrototypeID to use if also creating a backing prototype. Used when loading.</param>
		/// <returns>The new instanced mob</returns>
		public static Mob NewInstance(MobLevel level, bool withPrototype = false, uint? prototypeID = null, uint? inventoryPrototypeID = null, uint? equipmentPrototypeID = null)
		{
			Mob newMob;

			if (withPrototype)
				newMob = DataAccess.Get<Mob>(NewPrototype(level, prototypeID, inventoryPrototypeID, equipmentPrototypeID).Spawn(level, false), CacheType.Instance);
			else
			{
				newMob = DataAccess.Get<Mob>(DataAccess.Add<Mob>(new Mob(level), CacheType.Instance), CacheType.Instance);
				DataAccess.Add<Inventory>(newMob.Inventory, CacheType.Instance);
				DataAccess.Add<Inventory>(newMob.WornEquipment, CacheType.Instance);
			}

			return newMob;
		}

		/// <summary>
		/// Spawns an instance of the mob from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The spawned mob. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent cannot be null. Adds new mob to instanced room.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint? parent = null)
		{
			return SpawnAsObject<T>(Level, withEntities, parent);
		}

		/// <summary>
		/// Spawns an instance of the mob from prototype and adds it to the cache.
		/// </summary>
		/// <param name="level">The level of the mob to spawn</param>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The spawned mob. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent cannot be null. Adds new mob to instanced room.</remarks>
		public T SpawnAsObject<T>(MobLevel level, bool withEntities, uint? parent = null) where T: ICacheableObject
		{
			return DataAccess.Get<T>(Spawn(level, withEntities, parent), CacheType.Instance);
		}

		/// <summary>
		/// Spawns an instance of the mob from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The instance ID of the spawned mob. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new mob to parent room, if specified.</remarks>
		public override uint? Spawn(bool withEntities, uint? parent = null)
		{
			return Spawn(Level, withEntities, parent);
		}

		/// <summary>
		/// Spawns an instance of the mob from prototype and adds it to the cache.
		/// </summary>
		/// <param name="level">The level of the mob to spawn</param>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The instance ID of the spawned mob. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent may be null. Adds new mob to parent room, if specified.</remarks>
		public uint? Spawn(MobLevel level, bool withEntities, uint? parent = null)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			Logger.Info(nameof(Mob), nameof(Spawn), "Spawning mob: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance mob
			var newMob = NewInstance(level, false);

			// Retrieve parent room and add mob
			DataAccess.Get<Room>(parent, CacheType.Instance)?.AddEntity(newMob.Instance, newMob);

			// Set remaining properties
			newMob.Prototype = Prototype;
			CopyTo(newMob);

			// Spawn contained entities
			if (withEntities)
			{
				// Because NewInstance also adds Inventory and WornEquipment as instances but we want to spawn them, we need to remove the
				// the default new Inventory instances from the cache
				DataAccess.Remove<Inventory>(DataAccess.Get<Inventory>(newMob.Inventory.Instance, CacheType.Instance).Instance, CacheType.Instance);
				DataAccess.Remove<Inventory>(DataAccess.Get<Inventory>(newMob.WornEquipment.Instance, CacheType.Instance).Instance, CacheType.Instance);

				// Now spawn the Inventory and Equipment
				newMob.Inventory = DataAccess.Get<Inventory>(Inventory.Spawn(withEntities, Inventory.Prototype), CacheType.Instance);
				newMob.WornEquipment = DataAccess.Get<Inventory>(WornEquipment.Spawn(withEntities, Inventory.Prototype), CacheType.Instance);
			}

			Logger.Info(nameof(Mob), nameof(Spawn), "Finished spawning mob.");

			return newMob.Instance;
		}

		/// <summary>
		/// Copies this item's properties to another item.
		/// </summary>
		/// <param name="mob">The item to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public void CopyTo(Mob mob)
		{
			if (mob == null)
				return;

			base.CopyTo(mob);
			Behavior.CopyTo(mob.Behavior);
			mob.Level = Level;
		}

		protected override void OnCacheObjectRemoved(object source, CacheObjectEventArgs args)
		{
			// Check if the object removed was the Inventory or WornEquipment. If so, create a new cached object respectively.
			if (args.CacheType == CacheType.Instance)
			{
				if (Inventory.Instance == args.ID)
					Inventory = DataAccess.Get<Inventory>(DataAccess.Add<Inventory>(new Inventory(), CacheType.Instance), CacheType.Instance);

				if (WornEquipment.Instance == args.ID)
					WornEquipment = DataAccess.Get<Inventory>(DataAccess.Add<Inventory>(new Inventory(), CacheType.Instance), CacheType.Instance);
			}
			else
			{
				if (Inventory.Prototype == args.ID)
					Inventory = DataAccess.Get<Inventory>(DataAccess.Add<Inventory>(new Inventory(), CacheType.Prototype), CacheType.Prototype);

				if (WornEquipment.Prototype == args.ID)
					WornEquipment = DataAccess.Get<Inventory>(DataAccess.Add<Inventory>(new Inventory(), CacheType.Prototype), CacheType.Prototype);
			}
		}

		protected override void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{
			DataAccess.Remove<Inventory>(args.CacheType == CacheType.Instance ? Inventory.Instance : Inventory.Prototype, args.CacheType);
			DataAccess.Remove<Inventory>(args.CacheType == CacheType.Instance ? WornEquipment.Instance : WornEquipment.Prototype, args.CacheType);
		}
	}
}