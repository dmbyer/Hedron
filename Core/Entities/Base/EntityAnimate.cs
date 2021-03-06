﻿using Hedron.Core.Commands;
using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.Skills;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Slot;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Entities.Base
{
	/// <summary>
	/// For all living entities
	/// </summary>
	abstract public class EntityAnimate : Entity, IPools, IAttributes, IQualities
	{
		/// <summary>
		/// Eventhandler for entity death
		/// </summary>
		public event EventHandler<CacheObjectEventArgs> EntityDied;

		/// <summary>
		/// The entity's privilege level
		/// </summary>
		[JsonProperty]
		public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.NPC;

		/// <summary>
		/// Entity state
		/// </summary>
		[JsonProperty]
		public EntityState State { get; set; }

		/// <summary>
		/// The network connection ID
		/// </summary>
		[JsonIgnore]
		public string ConnectionID { get; set; }

		/// <summary>
		/// The entity's IO queues; should remain null for entity's without an associated network connection
		/// </summary>
		[JsonIgnore]
		public IOHandler IOHandler { get; set; }

		/// <summary>
		/// The entity's base attributes
		/// </summary>
		[JsonProperty]
		public Attributes BaseAttributes { get; set; } = Attributes.Default();

		/// <summary>
		/// The entity's learned skills
		/// </summary>
		[JsonProperty]
		public List<ISkill> Skills { get; set; } = new List<ISkill>();

		/// <summary>
		/// The entity's modified attributes
		/// </summary>
		[JsonIgnore]
		public virtual Attributes ModifiedAttributes
		{
			get
			{
				var modAttributes = new Attributes();
				var multAttributes = new Attributes();

				foreach (var Effect in Effects)
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multAttributes += Effect.Attributes;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modAttributes += Effect.Attributes;
				}

				foreach (var Effect in DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType).SelectMany(x => x.Effects))
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multAttributes += Effect.Attributes;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modAttributes += Effect.Attributes;
				}

				return (BaseAttributes + modAttributes) * multAttributes;
			}
		}

		/// <summary>
		/// The entity's base pools
		/// </summary>
		[JsonProperty]
		public Pools BaseMaxPools { get; set; } = Pools.Default();

		/// <summary>
		/// The entity's currency
		/// </summary>
		[JsonProperty]
		public Currency Currency { get; set; } = new Currency();

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
		public virtual Pools ModifiedPools
		{
			get
			{
				var modPools = new Pools();
				var multPools = new Pools();

				foreach (var Effect in Effects)
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multPools += Effect.Pools;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modPools += Effect.Pools;
				}

				foreach (var Effect in DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType).SelectMany(x => x.Effects))
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multPools += Effect.Pools;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modPools += Effect.Pools;
				}

				return (BaseMaxPools + modPools) * multPools;
			}
		}

		/// <summary>
		/// The entity's base qualities
		/// </summary>
		[JsonProperty]
		public Qualities BaseQualities { get; set; } = Qualities.Default();

		/// <summary>
		/// The entity's modified qualities
		/// </summary>
		[JsonIgnore]
		public virtual Qualities ModifiedQualities
		{
			get
			{
				var modQualities = new Qualities();
				var multQualities = new Qualities();

				foreach (var Effect in Effects)
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multQualities += Effect.Qualities;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modQualities += Effect.Qualities;
				}

				foreach (var Effect in DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType).SelectMany(x => x.Effects))
				{
					if (Effect.Attributes != null && Effect.Attributes.IsMultiplier)
						multQualities += Effect.Qualities;
					else if (Effect.Attributes != null && !Effect.Attributes.IsMultiplier)
						modQualities += Effect.Qualities;
				}

				return (BaseQualities + modQualities) * multQualities;
			}
		}

		/// <summary>
		/// The entity's worn equipment
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		protected EntityContainer _equipment = new EntityContainer();

		/// <summary>
		/// The entity's inventory
		/// </summary>
		[JsonConverter(typeof(EntityContainerPropertyConverter))]
		[JsonProperty]
		protected EntityContainer _inventory = new EntityContainer();

		/// <summary>
		/// Constructor
		/// </summary>
		public EntityAnimate() : base()
		{
			State = EntityState.Active;

			_equipment.EntityAdded += HandleItemEquipped;
			_equipment.EntityRemoved += HandleItemUnequipped;

			EntityDied += HandleDeath;

			ModifiedPools.CopyTo(CurrentPools);
		}

		/// <summary>
		/// Returns the item equipped at the specified slot
		/// </summary>
		/// <param name="slot">The slot to check for equipped items</param>
		/// <returns>A list of equipped items</returns>
		public EntityInanimate GetFirstItemEquippedAt(ItemSlot slot)
		{
			return DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType)
				?.FirstOrDefault(i => i.Slot == slot);
		}

		/// <summary>
		/// Returns all items equipped at the specified slots
		/// </summary>
		/// <param name="slot">The slots to check for equipped items</param>
		/// <returns>A list of equipped items, or an empty list if there are no matches.</returns>
		public List<EntityInanimate> GetItemsEquippedAt(params ItemSlot[] slot)
		{
			List<EntityInanimate> equippedItems = new List<EntityInanimate>();

			foreach (var itemSlot in slot)
				foreach (var item in DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType))
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
			return DataAccess.GetMany<EntityInanimate>(_equipment.GetAllEntities(), CacheType);
		}

		/// <summary>
		/// Returns all items in inventory
		/// </summary>
		/// <returns>A list of items in inventory</returns>
		public List<EntityInanimate> GetInventoryItems()
		{
			return DataAccess.GetMany<EntityInanimate>(_inventory.GetAllEntities(), CacheType);
		}

		/// <summary>
		/// Adds an item to inventory
		/// </summary>
		/// <param name="itemID">The item ID to add</param>
		public void AddInventoryItem(uint? itemID)
		{
			var item = DataAccess.Get<EntityInanimate>(itemID, CacheType);

			if (item != null)
				_inventory.AddEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
		}

		/// <summary>
		/// Remove a single inventory item
		/// </summary>
		/// <param name="itemID">The item ID to remove</param>
		/// <returns>The item that was removed</returns>
		public EntityInanimate RemoveInventoryItem(uint? itemID)
		{
			var removedItem = _inventory.GetEntity<EntityInanimate>(itemID);

			if (removedItem != null)
				_inventory.RemoveEntity(itemID);

			return removedItem;
		}

		/// <summary>
		/// Removes inventory items
		/// </summary>
		/// <param name="itemIDs">The item IDs to remove</param>
		/// <returns>The list of items that were removed</returns>
		public List<EntityInanimate> RemoveInventoryItems(List<uint?> itemIDs)
		{
			var removedItems = DataAccess.GetMany<EntityInanimate>(_inventory.GetAllEntities(), CacheType)
				.Where(i => CacheType == CacheType.Instance ? itemIDs.Contains(i.Instance) : itemIDs.Contains(i.Prototype))
				.ToList();

			foreach (var item in removedItems)
				_inventory.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype);

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
				_equipment.AddEntity(itemID, item);
			}
			else if (swapRequired && swap)
			{
				// TODO: Fix so this only removes the required number of items from the slots
				swappedItems = RemoveItemsAt(slot);
				_equipment.AddEntity(itemID, item);
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
			var removedItem = _equipment.GetEntity<EntityInanimate>(itemID);

			if (removedItem != null)
			{
				_equipment.RemoveEntity(itemID);
				_inventory.AddEntity(CacheType == CacheType.Instance ? removedItem.Instance : removedItem.Prototype, removedItem);
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
				_equipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
				_inventory.AddEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
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
				_equipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
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
				_equipment.RemoveEntity(CacheType == CacheType.Instance ? item.Instance : item.Prototype, item);
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
				var died = CurrentPools.HitPoints <= 0;

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

		/// <summary>
		/// Improves an existing skill. If the skill is not already in the entity's list, it will be added prior to improving.
		/// </summary>
		/// <param name="skillName">The friendly name of the skill to add</param>
		/// <param name="successChance">The chance of success this skill had of activating; 1.0 denotes a 100% chance.</param>
		/// <returns>The result of the skill improvement</returns>
		/// <remarks>The successChance will be clamped between 0.01 and 1.0.</remarks>
		public ImproveSkillResult ImproveSkill(string skillName, double successChance)
		{
			double improvedBy;
			string improvedMessage;
			bool wasAdded = false;
			skillName = skillName.ToLower();
			Type skillType = SkillMap.FriendlyNameToSkill(skillName);
			var skill = Skills.FirstOrDefault(s => s.GetType() == skillType);

			// Clamp success chance
			if (successChance > 1.0)
				successChance = 1.0;

			if (successChance < 0)
				successChance = 0.01;

			// Add or improve skill
			if (skill == default(ISkill))
			{
				skill = (ISkill)Activator.CreateInstance(skillType);
				Skills.Add(skill);
				wasAdded = true;
				skill.SkillLevel = 1;
				improvedBy = 1;
				improvedMessage = $"You have learned the {skillName} skill!";
			}
			else
			{
				// Without the default case, the compiler complains the switch is not exhaustive for some reason...
				double exp = successChance switch
				{
					double c when c >  0.95               => 1,
					double c when c >  0.75 && c <= 0.95  => 2,
					double c when c >  0.25 && c <= 0.75  => 3,
					double c when c >= 0.05 && c <= 0.25  => 4,
					double c when c <  0.05               => 5,
					_ => 1
				};

				// ... so log in the default case for debugging purposes.
				Logger.Bug(nameof(EntityAnimate), nameof(ImproveSkill), "Skill improvement experience calculated as the default case.");

				improvedBy = exp * skill.LearnRate;
				skill.SkillLevel += improvedBy;
				improvedMessage = $"Your {skillName} skill has improved!";
			}

			return new ImproveSkillResult(improvedBy, improvedMessage, wasAdded, skillName);
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
		/// Method to handle item equipped event
		/// </summary>
		/// <param name="args">The event args</param>
		public void HandleItemEquipped(object source, CacheObjectEventArgs args)
		{

		}

		/// <summary>
		/// Method to handle item unequipped event
		/// </summary>
		/// <param name="args">The event args</param>
		public void HandleItemUnequipped(object source, CacheObjectEventArgs args)
		{
			if (CurrentPools.HitPoints > BaseMaxPools.HitPoints)
				ModifyCurrentHealth(0 - (int)(CurrentPools.HitPoints - BaseMaxPools.HitPoints), false);

			if (CurrentPools.Energy > BaseMaxPools.Energy)
				ModifyCurrentEnergy(0 - (int)(CurrentPools.Energy - BaseMaxPools.Energy), false);

			if (CurrentPools.Stamina > BaseMaxPools.Stamina)
				ModifyCurrentStamina(0 - (int)(CurrentPools.Stamina - BaseMaxPools.Stamina), false);
		}

		/// <summary>
		/// Handles death for the entity. Does not remove from cache.
		/// </summary>
		protected virtual void HandleDeath(object source, CacheObjectEventArgs args)
		{
			if (args.ID == Instance)
			{
				// TODO: Handle soulbound items for players
				var newCorpse = new Storage
				{
					Name = "corpse " + Name,
					ShortDescription = $"{ShortDescription}'s corpse.",
					LongDescription = $"{ShortDescription}'s corpse.",
				};
				newCorpse.Tier.Level = Tier.Level;

				DataAccess.Add<Storage>(newCorpse, CacheType.Instance);

				foreach (var invItem in GetInventoryItems().Concat(GetEquippedItems()))
					newCorpse.AddEntity(invItem.Instance, invItem);

				_inventory.RemoveAllEntities();
				_equipment.RemoveAllEntities();

				GetInstanceParentRoom()?.StorageItems.AddEntity(newCorpse.Instance, newCorpse, false);
			}
			else
			{
				// TODO: Does HandleDeath need to allow for situations where the entity itself isn't the one dying?
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

			BaseMaxPools.CopyTo(entityAnimate.BaseMaxPools);
			BaseAttributes.CopyTo(entityAnimate.BaseAttributes);
			BaseQualities.CopyTo(entityAnimate.BaseQualities);
			Currency.CopyTo(entityAnimate.Currency);
		}
	}
}