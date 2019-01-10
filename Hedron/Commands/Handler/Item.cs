﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;
using Hedron.Data;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Reviews the contents of worn equipment
		/// </summary>
		private static CommandResult Equipment(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Equipment), ex.Message);
				return CommandResult.NullEntity();
			}

			var output = new OutputBuilder("Equipment: ");

			// Player-only command to view current room.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			// Get all items being worn, sorted alphabetically
			var wornItems = entity.GetEquippedItems()
				.OrderBy(item => item.Name)
				.ToList();

			if (wornItems.Count == 0)
				output.Append("You aren't wearing anything.");
			else
			{

				// Initialize item slot column
				var slots = new List<string>();
				foreach (var slot in Enum.GetValues(typeof(ItemSlot)))
					slots.Add(slot.ToString());

				// Determine width of item slot column
				int maxSlotTextWidth = 0;
				foreach (var slot in slots)
					if (slot.Length > maxSlotTextWidth)
						maxSlotTextWidth = slot.Length;

				// Pad item slot column
				maxSlotTextWidth += 3;

				// Initialize equipment list table
				var mappedEquipment = new Dictionary<ItemSlot, List<string>>();
				foreach (var slot in Enum.GetValues(typeof(ItemSlot)))
					mappedEquipment.Add((ItemSlot)slot, new List<string>());

				// Build out descriptions for worn equipment
				foreach (var slot in mappedEquipment)
				{
					foreach (var item in wornItems)
					{
						if (item.Slot == slot.Key)
							mappedEquipment[slot.Key].Add(item.ShortDescription);
					}
				}

				// Print a formatted table of worn equipment
				foreach (var slot in mappedEquipment)
				{
					foreach (var item in slot.Value)
					{
						output.Append(
							string.Format("{0," + maxSlotTextWidth + "}: {1}", slot.Key.ToString(), item));
					}
				}
			}
			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Gets an object from a container
		/// </summary>
		private static CommandResult Get(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Get), ex.Message);
				return CommandResult.NullEntity();
			}

			var nameToGet = ParseFirstArgument(argument).ToUpper();

			if (nameToGet == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to get?");

			// Search room for a match
			var room = EntityContainer.GetInstanceParent<Room>(entity.Instance);
			var roomEntities = DataAccess.GetMany<EntityInanimate>(room?.GetAllEntities(), CacheType.Instance);

			if (roomEntities.Count == 0)
			{
				// There are no items in the room to pick up
				if (nameToGet == "ALL")
					return new CommandResult(ResultCode.SUCCESS, "There is nothing to pick up.");
				else
					return new CommandResult(ResultCode.FAIL, "You don't see that.");
			}

			List<EntityInanimate> matchedItems;
			var output = new OutputBuilder();

			if (nameToGet == "ALL")
			{
				matchedItems = roomEntities;
			}
			else
			{
				matchedItems = new List<EntityInanimate>();
				EntityInanimate itemMatched = roomEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToGet));
				if (itemMatched != null)
					matchedItems.Add(itemMatched);
			}

			if (matchedItems.Count == 0)
				return new CommandResult(ResultCode.FAIL, "You don't see that.");

			foreach (var item in matchedItems)
			{
				room.RemoveEntity(item.Instance, item);
				entity.AddInventoryItem(item.Instance);
			}

			if (matchedItems.Count == 1)
			{
				output.Append("You pick up " + matchedItems[0].ShortDescription + ".");
			}
			else
			{
				var itemsPicked = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(matchedItems, EntityQuantityMapper.MapStringTypes.ShortDescription);

				output.Append("You pick up:");
				output.Append(TextFormatter.NewTableFromList(itemsPicked, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Drops an object to a room
		/// </summary>
		private static CommandResult Drop(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Drop), ex.Message);
				return CommandResult.NullEntity();
			}

			var nameToDrop = ParseFirstArgument(argument).ToUpper();

			if (nameToDrop == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to drop?");

			if (EntityContainer.GetInstanceParent<Room>(entity.Instance) == null)
				return CommandResult.Failure("There is nowhere to drop it to.");

			// Search inventory for a match
			var room = EntityContainer.GetInstanceParent<Room>(entity.Instance);
			var inventoryEntities = entity.GetInventoryItems();

			if (inventoryEntities.Count == 0)
			{
				if (nameToDrop == "ALL")
					return CommandResult.Failure("You don't have anything to drop.");
				else
					return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that.");
			}

			List<EntityInanimate> matchedItems;
			var output = new OutputBuilder();

			if (nameToDrop == "ALL")
			{
				matchedItems = inventoryEntities;
			}
			else
			{
				matchedItems = new List<EntityInanimate>();
				EntityInanimate itemMatched = inventoryEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToDrop));
				if (itemMatched != null)
					matchedItems.Add(itemMatched);
			}

			if (matchedItems.Count == 0)
				return CommandResult.Failure("You don't have that.");

			foreach (var item in matchedItems)
			{
				entity.RemoveInventoryItem(item.Instance);
				room.AddEntity(item.Instance, item);
			}

			if (matchedItems.Count == 1)
			{
				output.Append("You drop " + matchedItems[0].ShortDescription + ".");
			}
			else
			{
				var droppedItems = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(matchedItems, EntityQuantityMapper.MapStringTypes.ShortDescription);

				output.Append("You drop:");
				output.Append(TextFormatter.NewTableFromList(droppedItems, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Reviews the contents of the inventory
		/// </summary>
		private static CommandResult Inventory(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Inventory), ex.Message);
				return CommandResult.NullEntity();
			}

			// Player-only command to view current room.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var output = new OutputBuilder("Inventory: ");
			var entities = entity.GetInventoryItems();

			if (entities.Count == 0)
			{
				output.Append("You aren't carrying anything.");
			}
			else
			{
				var itemDescriptions = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(entities, EntityQuantityMapper.MapStringTypes.ShortDescription);
				output.Append(TextFormatter.NewTableFromList(itemDescriptions, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Removes an object
		/// </summary>
		private static CommandResult Remove(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Remove), ex.Message);
				return CommandResult.NullEntity();
			}

			var nameToRemove = ParseFirstArgument(argument).ToUpper();

			if (nameToRemove == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to remove?");

			// Search equipment for a match
			var equipmentEntities = entity.GetEquippedItems();

			if (equipmentEntities.Count == 0)
			{
				if (nameToRemove == "ALL")
					return CommandResult.Failure("You aren't wearing anything.");
				else
					return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that equipped.");
			}

			List<EntityInanimate> matchedItems;

			if (nameToRemove == "ALL")
				matchedItems = equipmentEntities;
			else
			{
				matchedItems = new List<EntityInanimate>();
				EntityInanimate itemMatched = equipmentEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToRemove));
				if (itemMatched != null)
					matchedItems.Add(itemMatched);
			}

			if (matchedItems.Count == 0)
				return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that equipped.");

			var output = new OutputBuilder();

			foreach (var item in matchedItems)
			{
				entity.UnequipItem(item.Instance);
				output.Append($"You remove {item.ShortDescription} and place it in your pack.");
			}

			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Wears an object
		/// </summary>
		private static CommandResult Wear(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Wear), ex.Message);
				return CommandResult.NullEntity();
			}

			var nameToWear = ParseFirstArgument(argument).ToUpper();

			if (nameToWear == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to wear?");

			// Search inventory for a match
			var inventoryEntities = entity.GetInventoryItems();

			if (inventoryEntities.Count == 0)
			{
				if (nameToWear == "ALL")
					return CommandResult.Failure("You don't have anything to wear.");
				else
					return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that.");
			}

			var output = new OutputBuilder();
			List<EntityInanimate> itemsMarkedAsWorn = new List<EntityInanimate>();

			if (nameToWear == "ALL")
			{
				// A list of items that will be removed from inventory after they have been worn
				List<EntityInanimate> itemsToRemoveFromInventory = new List<EntityInanimate>();

				// Find each item in the inventory that can be worn, and if there is still room in the corresponding slot,
				// equip it.
				foreach (var item in inventoryEntities)
				{
					try
					{
						entity.EquipItemAt(item.Instance, item.Slot, false);
						entity.RemoveInventoryItem(item.Instance);
						itemsMarkedAsWorn.Add(item);
					}
					catch (SlotFullException)
					{
						continue;
					}
				}
			}
			else
			{
				// Find the given item in inventory and equip it, removing any currently equipped items as necessary
				List<EntityInanimate> itemsRemoved = new List<EntityInanimate>();
				EntityInanimate itemMatched = inventoryEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToWear));
				if (itemMatched != null)
				{
					try
					{
						// Equip the item and swap already equipped items to inventory
						itemsRemoved = entity.EquipItemAt(itemMatched.Instance, itemMatched.Slot, true);

						entity.RemoveInventoryItem(itemMatched.Instance);
						itemsMarkedAsWorn.Add(itemMatched);

						foreach (var item in itemsRemoved)
							output.Append($"You remove {item.ShortDescription} and put it in your pack.");
					}
					catch (InvalidSlotException)
					{
						// Only equip wearable items
						return CommandResult.Failure("You can't equip that.");
					}
				}
				else
				{
					// The item requested to be removed was not found
					return CommandResult.Failure("You don't have that.");
				}
			}

			// Reorder list
			itemsMarkedAsWorn = itemsMarkedAsWorn.OrderBy(i => i.Name).ThenBy(i => i.ShortDescription).ToList();

			// Parse output based on items worn
			if (itemsMarkedAsWorn.Count == 0)
			{
				output.Append("There is nothing more for you to wear.");
			}
			else
			{
				foreach (var item in itemsMarkedAsWorn)
				{
					switch (item.Slot)
					{
						case ItemSlot.Light:
							output.Append($"You equip {item.ShortDescription} as your lightsource.");
							break;
						case ItemSlot.Orbit:
							output.Append($"You release {item.ShortDescription} in your orbit.");
							break;
						case ItemSlot.OneHandedWeapon:
							output.Append($"You equip {item.ShortDescription} as your weapon.");
							break;
						case ItemSlot.TwoHandedWeapon:
							output.Append($"You equip {item.ShortDescription} as your weapon.");
							break;
						case ItemSlot.Shield:
							output.Append($"You equip {item.ShortDescription} as your shield.");
							break;
						default:
							output.Append($"You wear {item.ShortDescription} upon your {item.Slot.ToString().ToLower()}.");
							break;
					}
				}
			}

			return CommandResult.Success(output.Output);
		}
	}
}