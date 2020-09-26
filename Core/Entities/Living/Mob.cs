using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Factory;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Locale;
using Newtonsoft.Json;
using System;

namespace Hedron.Core.Entities.Living
{
	public sealed class Mob : EntityAnimate
	{
		/// <summary>
		/// The mob's behavior
		/// </summary>
		public MobBehavior Behavior { get; set; } = new MobBehavior();

		/// <summary>
		/// The Effect for the mob's level
		/// </summary>
		[JsonProperty]
		private Effect LevelEffect { get; set; } = new Effect();

		private MobLevel _level;

		/// <summary>
		/// Default constructor
		/// </summary>
		public Mob() 
			: base()
		{
			LevelEffect = Effect.NewMultiplier(MobLevelModifier.Map(MobLevel.Fair));
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
			LevelEffect = Effect.NewMultiplier(MobLevelModifier.Map(level));
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
					LevelEffect = Effect.NewMultiplier(MobLevelModifier.Map(value));
					_level = value;
				}
				catch
				{
					LevelEffect = Effect.NewMultiplier(MobLevelModifier.Map(MobLevel.Fair));
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
				return base.ModifiedAttributes * LevelEffect.Attributes;
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
				return base.ModifiedPools * LevelEffect.Pools;
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
				return base.ModifiedQualities * LevelEffect.Qualities;
			}
		}

		/// <summary>
		/// Creates a new mob and adds it to the Prototype cache
		/// </summary>
		/// <param name="parentID">The parent ID of the room to add this mob to; may be null</param>
		/// <param name="level">The optional level of the mob</param>
		/// <returns>The new prototype mob</returns>
		public static Mob NewPrototype(uint? parentID, MobLevel level = MobLevel.Fair)
		{
			var newMob = new Mob(level);
			newMob._inventory.CacheType = CacheType.Prototype;
			newMob._equipment.CacheType = CacheType.Prototype;

			var room = DataAccess.Get<Room>(parentID, CacheType.Prototype);
			DataAccess.Add<EntityContainer>(newMob._inventory, CacheType.Prototype);
			DataAccess.Add<EntityContainer>(newMob._equipment, CacheType.Prototype);
			DataAccess.Add<Mob>(newMob, CacheType.Prototype);

			if (room != null)
				room.Animates.AddEntity(newMob.Prototype, newMob, true);

			DataPersistence.SaveObject(newMob);
			return newMob;
		}

		/// <summary>
		/// Creates a new mob and adds it to the Instance cache
		/// </summary>
		/// <param name="withPrototype">Whether to also create a backing prototype</param>
		/// <param name="parentID">The parent ID of the room to add this mob to; may be null</param>
		/// <returns>The new instanced mob</returns>
		public static Mob NewInstance(bool withPrototype, uint? parentID, MobLevel level = MobLevel.Fair)
		{
			Mob newMob;
			var instanceRoom = DataAccess.Get<Room>(parentID, CacheType.Instance);

			if (withPrototype)
			{
				var protoRoom = DataAccess.Get<Room>(instanceRoom.Prototype, CacheType.Prototype);

				var newProtoMob = NewPrototype((uint)protoRoom.Prototype, level);
				protoRoom?.Animates.AddEntity(newProtoMob.Prototype, newProtoMob, true);

				newMob = newProtoMob.SpawnWithoutParent(level, false);
			}
			else
			{
				newMob = DataAccess.Get<Mob>(DataAccess.Add<Mob>(new Mob(), CacheType.Instance), CacheType.Instance);
				DataAccess.Add<EntityContainer>(newMob._inventory, CacheType.Instance);
				DataAccess.Add<EntityContainer>(newMob._equipment, CacheType.Instance);
			}

			instanceRoom?.Animates.AddEntity(newMob.Instance, newMob, false);
			return newMob;
		}

		/// <summary>
		/// Spawns an instance of the mob from prototype and adds it to the cache.
		/// </summary>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <param name="parent">The parent room instance ID</param>
		/// <returns>The spawned mob. Will return null if the method is called from an instanced object.</returns>
		/// <remarks>Parent cannot be null. Adds new mob to instanced room.</remarks>
		public override T SpawnAsObject<T>(bool withEntities, uint parent)
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
		public T SpawnAsObject<T>(MobLevel level, bool withEntities, uint parent) where T: CacheableObject
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
		public override uint? Spawn(bool withEntities, uint parent)
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
		public uint? Spawn(MobLevel level, bool withEntities, uint parent)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			var parentContainer = DataAccess.Get<EntityContainer>(parent, CacheType.Instance);
			var parentRoom = DataAccess.Get<Room>(parentContainer.InstanceParent, CacheType.Instance);

			if (parentContainer == null || parentRoom == null)
				throw new LocaleException("Parent cannot be null when spawning a mob.");

			Logger.Info(nameof(Mob), nameof(Spawn), "Spawning mob: " + Name + ": ProtoID=" + Prototype.ToString());

			// Create new instance mob and add to parent room
			var newMob = SpawnWithoutParent(level, withEntities);
			DataAccess.Get<Room>(parentRoom.Instance, CacheType.Instance).Animates.AddEntity(newMob.Instance, newMob, false);

			Logger.Info(nameof(Mob), nameof(Spawn), "Finished spawning mob.");

			return newMob.Instance;
		}

		/// <summary>
		/// Spawns a mob from a prototype without a parent
		/// </summary>
		/// <param name="level">The level of the mob to spawn</param>
		/// <param name="withEntities">Whether to also spawn contained entities</param>
		/// <returns>The new mob</returns>
		private Mob SpawnWithoutParent(MobLevel level, bool withEntities)
		{
			if (CacheType != CacheType.Prototype)
				return null;

			var newMob = DataAccess.Get<Mob>(DataAccess.Add<Mob>(new Mob(level), CacheType.Instance), CacheType.Instance);

			// Set remaining properties
			newMob.Prototype = Prototype;
			CopyTo(newMob);

			// Spawn contained entities
			if (withEntities)
			{
				newMob._inventory = _inventory.SpawnAsObject<EntityContainer>(!withEntities, (uint)newMob.Instance);
				foreach (var animate in _inventory.GetAllEntitiesAsObjects<EntityAnimate>())
					animate.Spawn(withEntities, (uint)newMob._inventory.Instance);

				newMob._equipment = _equipment.SpawnAsObject<EntityContainer>(!withEntities, (uint)newMob.Instance);
				foreach (var animate in _equipment.GetAllEntitiesAsObjects<EntityAnimate>())
					animate.Spawn(withEntities, (uint)newMob._equipment.Instance);
			}
			else
			{
				newMob._inventory = _inventory.SpawnAsObject<EntityContainer>(!withEntities, (uint)newMob.Instance);
				newMob._equipment = _equipment.SpawnAsObject<EntityContainer>(!withEntities, (uint)newMob.Instance);
			}

			return newMob;
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

		protected override void OnObjectDestroyed(object source, CacheObjectEventArgs args)
		{
			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? _inventory.Instance : _inventory.Prototype, args.CacheType);
			DataAccess.Remove<EntityContainer>(args.CacheType == CacheType.Instance ? _equipment.Instance : _equipment.Prototype, args.CacheType);
		}
	}
}