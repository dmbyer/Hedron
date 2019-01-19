using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.Commands;
using Hedron.System;
using Hedron.Data;
using Hedron.Core.Container;
using Hedron.Core.Property;
using Hedron.Network;
using Hedron.System.Exceptions;
using Newtonsoft.Json;

namespace Hedron.Core.Entity
{
	/// <summary>
	/// For all living entities
	/// </summary>
	abstract public class EntityAnimate : EntityBase, IPools, IAttributes, IQualities
	{
		/// <summary>
		/// Eventhandler for entity death
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityDied;

		/// <summary>
		/// The entity's privilege level
		/// </summary>
		public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.NPC;

		// State
		[JsonIgnore]
		public StateHandler StateHandler { get; set; } = new StateHandler();

		/// <summary>
		/// The entity's base attributes
		/// </summary>
		[JsonProperty]
		public Attributes BaseAttributes { get; set; } = Attributes.Default();

		/// <summary>
		/// The entity's modified attributes
		/// </summary>
		[JsonIgnore]
		public Attributes ModifiedAttributes
		{
			get
			{
				var modAttributes = new Attributes();
				var multAttributes = new Attributes();

				foreach (var affect in Affects)
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multAttributes += affect.Attributes;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modAttributes += affect.Attributes;
				}

				foreach (var affect in DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType).SelectMany(x => x.Affects))
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multAttributes += affect.Attributes;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modAttributes += affect.Attributes;
				}

				return (BaseAttributes + modAttributes) * multAttributes;
			}
		}

		/// <summary>
		/// The entity's base pools
		/// </summary>
		[JsonProperty]
		public Pools BaseMaxPools    { get; set; } = Pools.Default();

		/// <summary>
		/// The entity's current hitpoints
		/// </summary>
		[JsonProperty]
		public int CurrentHitPoints
		{
			get
			{
				return (int)CurrentPools.HitPoints;
			}
		}

		/// <summary>
		/// The entity's current stamina
		/// </summary>
		[JsonProperty]
		public int CurrentStamina
		{
			get
			{
				return (int)CurrentPools.Stamina;
			}
		}

		/// <summary>
		/// The entity's current energy
		/// </summary>
		[JsonProperty]
		public int CurrentEnergy
		{
			get
			{
				return (int)CurrentPools.Energy;
			}
		}

		/// <summary>
		/// The entity's current pools
		/// </summary>
		[JsonProperty]
		protected Pools CurrentPools { get; set; } = Pools.Default();

		/// <summary>
		/// The entity's modified pools
		/// </summary>
		[JsonIgnore]
		public Pools ModifiedPools
		{
			get
			{
				var modPools = new Pools();
				var multPools = new Pools();

				foreach (var affect in Affects)
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multPools += affect.Pools;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modPools += affect.Pools;
				}

				foreach (var affect in DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType).SelectMany(x => x.Affects))
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multPools += affect.Pools;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modPools += affect.Pools;
				}

				return (BaseMaxPools + modPools) * multPools;
			}
		}

		/// <summary>
		/// The entity's base qualities
		/// </summary>
		[JsonProperty]
		public Qualities BaseQualities  { get; set; } = Qualities.Default();

		/// <summary>
		/// The entity's modified qualities
		/// </summary>
		[JsonIgnore]
		public Qualities ModifiedQualities
		{
			get
			{
				var modQualities = new Qualities();
				var multQualities = new Qualities();

				foreach (var affect in Affects)
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multQualities += affect.Qualities;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modQualities += affect.Qualities;
				}

				foreach (var affect in DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType).SelectMany(x => x.Affects))
				{
					if (affect.Attributes != null && affect.Attributes.IsMultiplier)
						multQualities += affect.Qualities;
					else if (affect.Attributes != null && !affect.Attributes.IsMultiplier)
						modQualities += affect.Qualities;
				}

				return (BaseQualities + modQualities) * multQualities;
			}
		}

		/// <summary>
		/// The entity's worn equipment
		/// </summary>
		[JsonConverter(typeof(InventoryPropertyConverter))]
		[JsonProperty]
		protected Inventory WornEquipment { get; set; } = new Inventory();

		/// <summary>
		/// The entity's inventory
		/// </summary>
		[JsonConverter(typeof(InventoryPropertyConverter))]
		[JsonProperty]
		protected Inventory Inventory { get; set; } = new Inventory();

		/// <summary>
		/// Constructor
		/// </summary>
		public EntityAnimate() : base()
		{
			StateHandler.State = GameState.Active;

			WornEquipment.AffectAdded += HandleAffectAdded;
			WornEquipment.AffectRemoved += HandleAffectRemoved;
			WornEquipment.EntityAdded += HandleEntityAdded;
			WornEquipment.EntityRemoved += HandleEntityRemoved;

			EntityDied += HandleDeath;
		}

		/// <summary>
		/// Returns the item equipped at the specified slot
		/// </summary>
		/// <param name="slot">The slot to check for equipped items</param>
		/// <returns>A list of equipped items</returns>
		public EntityInanimate GetFirstItemEquippedAt(ItemSlot slot)
		{
			return DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType)
				?.FirstOrDefault(i => i.Slot == slot);
		}

		/// <summary>
		/// Returns all items equipped at the specified slots
		/// </summary>
		/// <param name="slot">The slots to check for equipped items</param>
		/// <returns>A list of equipped items</returns>
		public List<EntityInanimate> GetItemsEquippedAt(params ItemSlot[] slot)
		{
			List<EntityInanimate> equippedItems = new List<EntityInanimate>();

			foreach (var itemSlot in slot)
				foreach (var item in DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType))
					if (item.Slot == itemSlot)
						equippedItems.Add(item);

			return equippedItems;
		}

		/// <summary>
		/// Returns all items equipped
		/// </summary>
		/// <returns>A list of equipped items</returns>
		public List<EntityInanimate> GetEquippedItems()
		{
			return DataAccess.GetMany<EntityInanimate>(WornEquipment.GetAllEntities(), CacheType);
		}

		/// <summary>
		/// Returns all items in inventory
		/// </summary>
		/// <returns>A list of items in inventory</returns>
		public List<EntityInanimate> GetInventoryItems()
		{
			return DataAccess.GetMany<EntityInanimate>(Inventory.GetAllEntities(), CacheType);
		}

		/// <summary>
		/// Adds an item to inventory
		/// </summary>
		/// <param name="itemID">The item ID to add</param>
		public void AddInventoryItem(uint? itemID)
		{
			var item = DataAccess.Get<EntityInanimate>(itemID, CacheType);

			if (item != null)
				Inventory.AddEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
		}

		/// <summary>
		/// Remove a single inventory item
		/// </summary>
		/// <param name="itemID">The item ID to remove</param>
		/// <returns>The item that was removed</returns>
		public EntityInanimate RemoveInventoryItem(uint? itemID)
		{
			var removedItem = Inventory.GetEntity<EntityInanimate>(itemID);

			if (removedItem != null)
				Inventory.RemoveEntity(itemID);

			return removedItem;
		}

		/// <summary>
		/// Removes inventory items
		/// </summary>
		/// <param name="itemIDs">The item IDs to remove</param>
		/// <returns>The list of items that were removed</returns>
		public List<EntityInanimate> RemoveInventoryItems(List<uint?> itemIDs)
		{
			var removedItems = DataAccess.GetMany<EntityInanimate>(Inventory.GetAllEntities(), CacheType)
				.Where(i => CacheType == CacheType.Instance ? itemIDs.Contains(i.Instance) : itemIDs.Contains(i.Prototype))
				.ToList();

			foreach (var item in removedItems)
				Inventory.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype);

			return removedItems;
		}

		/// <summary>
		/// Equips an item
		/// </summary>
		/// <param name="item">The item to equip</param>
		/// <param name="slot">The slot to equip the item at</param>
		/// <param name="swap">Whether to swap the items if the slot is already full</param>
		/// <returns>The items removed as part of the swap</returns>
		/// <exception cref="InvalidSlotException">The item was attempted to be equipped at a slot it cannot be equipped at</exception>
		/// <exception cref="SlotFullException">If swap was false, the slot was full</exception>
		public List<EntityInanimate> EquipItemAt(uint? itemID, ItemSlot slot, bool swap)
		{
			var item = DataAccess.Get<EntityInanimate>(itemID, CacheType);
			var swappedItems = new List<EntityInanimate>();

			if (item == null)
				return swappedItems;

			if (item.Slot != slot)
				throw new InvalidSlotException();

			var swapRequired = false;
			var itemsAtSlot = GetItemsEquippedAt(slot);

			if (itemsAtSlot.Count >= Constants.MAX_EQUIPPED.AT(slot))
				swapRequired = true;

			if (!swapRequired)
			{
				WornEquipment.AddEntity(itemID, item);
				item.AffectAdded += HandleAffectAdded;
				item.AffectRemoved += HandleAffectRemoved;
			}
			else if (swapRequired && swap)
			{
				// TODO: Fix so this only removes the required number of items from the slots
				swappedItems = RemoveItemsAt(slot);

				foreach (var rItem in swappedItems)
				{
					rItem.AffectAdded -= HandleAffectAdded;
					rItem.AffectRemoved -= HandleAffectRemoved;
				}

				WornEquipment.AddEntity(itemID, item);
				item.AffectAdded += HandleAffectAdded;
				item.AffectRemoved += HandleAffectRemoved;
			}
			else if (swapRequired && !swap)
			{
				if (swapRequired)
					throw new SlotFullException();
			}

			return swappedItems;
		}

		/// <summary>
		/// Unequips an item and moves to inventory
		/// </summary>
		/// <param name="itemID">The item ID to move to inventory</param>
		/// <returns>The item that was moved to inventory</returns>
		public EntityInanimate UnequipItem(uint? itemID)
		{
			var removedItem = WornEquipment.GetEntity<EntityInanimate>(itemID);

			if (removedItem != null)
			{
				WornEquipment.RemoveEntity(itemID);
				Inventory.AddEntity(CacheType == CacheType.Instance ? removedItem.Instance : removedItem.Prototype, removedItem);
			}

			return removedItem;
		}

		/// <summary>
		/// Unequips all items and moves to inventory
		/// </summary>
		/// <param name="slot">The slots to check for equipped items</param>
		/// <returns>The list of items that were moved to inventory</returns>
		public void UnequipItemsAt(params ItemSlot[] slot)
		{
			var removedItems = GetItemsEquippedAt(slot);

			foreach (var item in removedItems)
			{
				item.AffectAdded -= HandleAffectAdded;
				item.AffectRemoved -= HandleAffectRemoved;

				WornEquipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
				Inventory.AddEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
			}
		}

		/// <summary>
		/// Removes the first equipped item of the given slot
		/// </summary>
		/// <param name="slot">The slot to remove the item from</param>
		/// <returns>The item that was removed</returns>
		public List<EntityInanimate> RemoveFirstItem(ItemSlot slot)
		{
			var removedItems = GetItemsEquippedAt(slot);

			foreach (var item in removedItems)
			{
				item.AffectAdded -= HandleAffectAdded;
				item.AffectRemoved -= HandleAffectRemoved;

				WornEquipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
			}

			return removedItems;
		}

		/// <summary>
		/// Removes equipped items
		/// </summary>
		/// <param name="slot">The slots to remove items from</param>
		/// <returns>The list of items that were removed</returns>
		public List<EntityInanimate> RemoveItemsAt(params ItemSlot[] slot)
		{
			var removedItems = GetItemsEquippedAt(slot);

			foreach (var item in removedItems)
			{
				item.AffectAdded -= HandleAffectAdded;
				item.AffectRemoved -= HandleAffectRemoved;

				WornEquipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
			}

			return removedItems;
		}

		/// <summary>
		/// Modifies the current health of entity by a set amount. Handles death if HP drops below 1.
		/// </summary>
		/// <param name="amount">The amount to modify health by.</param>
		/// <param name="allowBeyondLimit">Whether to allow the adjustment to go beyond min/max</param>
		/// <returns>The current health after modification and whether the entity died.</returns>
		public (int HitPoints, bool Died) ModifyCurrentHealth(int amount, bool allowBeyondLimit)
		{
			if (allowBeyondLimit)
			{
				var hp = (int)(CurrentPools.HitPoints += amount);
				var died = CurrentPools.HitPoints <= 0 ? true : false;

				if (died && CacheType == CacheType.Instance)
					OnDeath(new CacheObjectEventArgs((uint)Instance, CacheType.Instance));

				return (hp, died);
			}
			else
			{
				if (amount >= 0)
				{
					CurrentPools.HitPoints = CurrentPools.HitPoints + amount >= BaseMaxPools.HitPoints
						? BaseMaxPools.HitPoints
						: CurrentPools.HitPoints + amount;
				}
				else
				{
					CurrentPools.HitPoints = CurrentPools.HitPoints - amount <= 0
						? 1
						: CurrentPools.HitPoints - amount;
				}
			}

			return ((int)CurrentPools.HitPoints, false);
		}

		/// <summary>
		/// Modifies the current stamina of entity by a set amount.
		/// </summary>
		/// <param name="amount">The amount to modify stamina by.</param>
		/// <param name="allowBeyondLimit">Whether to allow the adjustment to go beyond min/max</param>
		/// <returns>The current stamina after modification.</returns>
		public int ModifyCurrentStamina(int amount, bool allowBeyondLimit)
		{
			if (allowBeyondLimit)
			{
				return (int)(CurrentPools.Stamina += amount);
			}
			else
			{
				if (amount >= 0)
				{
					CurrentPools.Stamina = CurrentPools.Stamina + amount >= BaseMaxPools.Stamina
						? BaseMaxPools.Stamina
						: CurrentPools.Stamina + amount;
				}
				else
				{
					CurrentPools.Stamina = CurrentPools.Stamina - amount <= 0
						? 1
						: CurrentPools.Stamina - amount;
				}
			}

			return (int)CurrentPools.Stamina;
		}

		/// <summary>
		/// Modifies the current energy of entity by a set amount.
		/// </summary>
		/// <param name="amount">The percentage to modify energy by.</param>
		/// <param name="allowBeyondLimit">Whether to allow the adjustment to go beyond min/max</param>
		/// <returns>The current energy after modification.</returns>
		public int ModifyCurrentEnergy(int amount, bool allowBeyondLimit)
		{
			if (allowBeyondLimit)
			{
				return (int)(CurrentPools.Energy += amount);
			}
			else
			{
				if (amount >= 0)
				{
					CurrentPools.Energy = CurrentPools.Energy + amount >= BaseMaxPools.Energy
						? BaseMaxPools.Energy
						: CurrentPools.Energy + amount;
				}
				else
				{
					CurrentPools.Energy = CurrentPools.Energy - amount <= 0
						? 1
						: CurrentPools.Energy - amount;
				}
			}

			return (int)CurrentPools.Energy;
		}


		public override void HandleAffectAdded(object source, AffectEventArgs args)
		{
			base.HandleAffectAdded(source, args);


		}


		public override void HandleAffectRemoved(object source, AffectEventArgs args)
		{
			base.HandleAffectRemoved(source, args);


		}


		public override void HandleEntityAdded(object source, CacheObjectEventArgs args)
		{
			base.HandleEntityAdded(source, args);
			
			/*
			// If an item was added to the worn equipment, process affects impacting Current Aspects
			if (source == WornEquipment)
			{
				var item = DataAccess.Get<EntityInanimate>(args.ID, CacheType);

				if (item == null)
					return;

				foreach (var affect in item.Affects)
				{
					CurrentAspects.HitPoints += affect.Aspects.HitPoints;
					CurrentAspects.Energy += affect.Aspects.Energy;
					CurrentAspects.Stamina += affect.Aspects.Stamina;
				}
			}
			*/
		}


		public override void HandleEntityRemoved(object source, CacheObjectEventArgs args)
		{
			base.HandleEntityRemoved(source, args);

			/*
			// If an item was removed from worn equipment, process affects impacting Current Aspects
			if (source == WornEquipment)
			{
				var item = DataAccess.Get<EntityInanimate>(args.ID, CacheType);

				if (item == null)
					return;

				foreach (var affect in item.Affects)
				{
					var hpVariance = ModifiedAspects.HitPoints - affect.Aspects.HitPoints;
					var enVariance = ModifiedAspects.Energy - affect.Aspects.Energy;
					var stVariance = ModifiedAspects.Stamina - affect.Aspects.Stamina;

					ModifyCurrentHealth((int)(ModifiedAspects.HitPoints - affect.Aspects.HitPoints), false);
					ModifyCurrentEnergy((int)(ModifiedAspects.Energy - affect.Aspects.Energy), false);
					ModifyCurrentStamina((int)(ModifiedAspects.Stamina - affect.Aspects.Stamina), false);
				}
			}
			*/
			if (source == WornEquipment)
			{
				if (CurrentPools.HitPoints > BaseMaxPools.HitPoints)
					ModifyCurrentHealth(0 - (int)(CurrentPools.HitPoints - BaseMaxPools.HitPoints), false);

				if (CurrentPools.Energy > BaseMaxPools.Energy)
					ModifyCurrentEnergy(0 - (int)(CurrentPools.Energy - BaseMaxPools.Energy), false);

				if (CurrentPools.Stamina > BaseMaxPools.Stamina)
					ModifyCurrentStamina(0 - (int)(CurrentPools.Stamina - BaseMaxPools.Stamina), false);
			}
		}

		/// <summary>
		/// Invokes the EntityDied event
		/// </summary>
		/// <param name="args">The event args</param>
		protected virtual void OnDeath(CacheObjectEventArgs args)
		{
			var handler = EntityDied;

			if (handler != null)
				EntityDied.Invoke(this, args);
		}

		/// <summary>
		/// Handles death for the entity. Does not remove from cache.
		/// </summary>
		protected virtual void HandleDeath(object source, CacheObjectEventArgs args)
		{
			if (args.ID == Instance)
			{
				// TODO: Handle soulbound items for players
				var newCorpse = new Corpse
				{
					Name = "corpse " + Name,
					ShortDescription = $"{ShortDescription}'s corpse.",
					LongDescription = $"{ShortDescription}'s corpse.",
				};
				newCorpse.Tier.Level = Tier.Level;

				DataAccess.Add<Corpse>(newCorpse, CacheType.Instance);

				foreach (var invItem in GetInventoryItems().Concat(GetEquippedItems()))
					newCorpse.AddEntity(invItem.Instance, invItem);

				Inventory.RemoveAllEntities();
				WornEquipment.RemoveAllEntities();

				EntityContainer.GetInstanceParent<Room>(Instance)?.AddEntity(newCorpse.Instance, newCorpse, false);
			}
			else
			{

			}
		}

		/// <summary>
		/// Copies this entity's properties to another entity.
		/// </summary>
		/// <param name="entityAnimate">The entity to copy to.</param>
		/// <remarks>Doesn't copy IDs or cache type.</remarks>
		public virtual void CopyTo(EntityAnimate entityAnimate)
		{
			if (entityAnimate == null)
				return;

			base.CopyTo(entityAnimate);

			entityAnimate.BaseMaxPools.CopyTo(BaseMaxPools);
			entityAnimate.BaseAttributes.CopyTo(BaseAttributes);
			entityAnimate.BaseQualities.CopyTo(BaseQualities);
		}
	}
}