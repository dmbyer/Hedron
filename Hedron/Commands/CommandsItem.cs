using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
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
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to view current room.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			entity.IOHandler.QueueOutput("Equipment: ");

			if (entity.Inventory.Count == 0)
				entity.IOHandler.QueueOutput("You aren't wearing anything.");
			else
			{
				// Get all items being worn, sorted alphabetically
				var wornItems = DataAccess.GetMany<EntityInanimate>(entity.WornEquipment.GetAllEntities(), CacheType.Instance)
					.OrderBy(item => item.Name);

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
						entity.IOHandler.QueueOutput(
							string.Format("{0," + maxSlotTextWidth + "}: {1}", slot.Key.ToString(), item));
					}
				}
			}
			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			var nameToGet = ParseFirstArgument(argument).ToUpper();

			if (nameToGet == "")
			{
				entity.IOHandler?.QueueOutput("What would you like to get?");
				return CommandResult.CMD_R_ERRSYNTAX;
			}

			// Search room for a match
			var room = DataAccess.Get<Room>(entity.Parent, CacheType.Instance);
			var roomEntities = DataAccess.GetMany<EntityInanimate>(room?.GetAllEntities(), CacheType.Instance);

			if (roomEntities.Count == 0)
			{
				// There are no items in the room to pick up
				entity.IOHandler?.QueueOutput(nameToGet == "ALL" ? "There is nothing to pick up." : "You don't see that.");
				return CommandResult.CMD_R_FAIL;
			}

			List<EntityInanimate> matchedItems;

			if (nameToGet == "ALL")
				matchedItems = roomEntities;
			else
			{
				matchedItems = new List<EntityInanimate>();
				EntityInanimate itemMatched = roomEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToGet));
				if (itemMatched != null)
					matchedItems.Add(itemMatched);
			}

			if (matchedItems.Count == 0)
			{
				entity.IOHandler?.QueueOutput("You don't see that.");
				return CommandResult.CMD_R_FAIL;
			}

			foreach (var item in matchedItems)
			{
				room.RemoveEntity(item.Instance, item);
				entity.Inventory.AddEntity(item.Instance, item);
			}

			if (matchedItems.Count == 1)
			{
				entity.IOHandler?.QueueOutput("You pick up " + matchedItems[0].ShortDescription + ".");
			}
			else
			{
				List<string> outputFormatted = new List<string>();
				EntityQuantityMapper.ParseEntityQuantitiesAsStrings(matchedItems, EntityQuantityMapper.MapStringTypes.ShortDescription)
					.ForEach(x => outputFormatted.Add("   " + x));

				entity.IOHandler?.QueueOutput("You pick up:");
				entity.IOHandler?.QueueOutput(string.Join("\n", outputFormatted));
			}

			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			var nameToDrop = ParseFirstArgument(argument).ToUpper();

			if (nameToDrop == "")
			{
				entity.IOHandler?.QueueOutput("What would you like to drop?");
				return CommandResult.CMD_R_ERRSYNTAX;
			}

			if (DataAccess.Get<Room>(entity.Parent, CacheType.Instance) == null)
			{
				entity.IOHandler?.QueueOutput("There is nowhere to drop it to.");
				return CommandResult.CMD_R_FAIL;
			}

			// Search inventory for a match
			var room = DataAccess.Get<Room>(entity.Parent, CacheType.Instance);
			var inventoryEntities = DataAccess.GetMany<EntityInanimate>(entity.Inventory?.GetAllEntities(), CacheType.Instance);

			if (inventoryEntities.Count == 0)
			{
				entity.IOHandler?.QueueOutput(nameToDrop == "ALL" ? "You don't have anything to drop." : "You don't have that.");
				return CommandResult.CMD_R_FAIL;
			}

			List<EntityInanimate> matchedItems;

			if (nameToDrop == "ALL")
				matchedItems = inventoryEntities;
			else
			{
				matchedItems = new List<EntityInanimate>();
				EntityInanimate itemMatched = inventoryEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToDrop));
				if (itemMatched != null)
					matchedItems.Add(itemMatched);
			}

			if (matchedItems.Count == 0)
			{
				entity.IOHandler?.QueueOutput("You don't have that.");
				return CommandResult.CMD_R_FAIL;
			}

			foreach (var item in matchedItems)
			{
				entity.Inventory.RemoveEntity(item.Instance, item);
				room.AddEntity(item.Instance, item);
			}

			if (matchedItems.Count == 1)
			{
				entity.IOHandler?.QueueOutput("You drop " + matchedItems[0].ShortDescription + ".");
			}
			else
			{
				List<string> outputFormatted = new List<string>();
				EntityQuantityMapper.ParseEntityQuantitiesAsStrings(matchedItems, EntityQuantityMapper.MapStringTypes.ShortDescription)
					.ForEach(x => outputFormatted.Add("   " + x));

				entity.IOHandler?.QueueOutput("You drop:");
				entity.IOHandler?.QueueOutput(string.Join("\n", outputFormatted));
			}

			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to view current room.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			entity.IOHandler.QueueOutput("Inventory: ");

			if (entity.Inventory.Count == 0)
				entity.IOHandler.QueueOutput("You aren't carrying anything.");
			else
			{
				var entities = DataAccess.GetMany<EntityInanimate>(entity.Inventory.GetAllEntities(), CacheType.Instance);
				var itemDescriptions = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(entities, EntityQuantityMapper.MapStringTypes.ShortDescription);
				foreach (var item in itemDescriptions)
					entity.IOHandler.QueueOutput("   " + item);
			}

			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			var nameToRemove = ParseFirstArgument(argument).ToUpper();

			if (nameToRemove == "")
			{
				entity.IOHandler?.QueueOutput("What would you like to remove?");
				return CommandResult.CMD_R_ERRSYNTAX;
			}

			// Search equipment for a match
			var equipmentEntities = DataAccess.GetMany<EntityInanimate>(entity.WornEquipment?.GetAllEntities(), CacheType.Instance);

			if (equipmentEntities.Count == 0)
			{
				entity.IOHandler?.QueueOutput(nameToRemove == "ALL" ? "You aren't wearing anything." : "You don't have that equipped.");
				return CommandResult.CMD_R_FAIL;
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
			{
				entity.IOHandler?.QueueOutput("You don't have that equipped.");
				return CommandResult.CMD_R_FAIL;
			}

			foreach (var item in matchedItems)
			{
				entity.WornEquipment.RemoveEntity(item.Instance, item);
				entity.Inventory.AddEntity(item.Instance, item);
				entity.IOHandler?.QueueOutput(string.Format("You remove {0} and place it in your pack.", item.Slot.ToString()));
			}

			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			var nameToWear = ParseFirstArgument(argument).ToUpper();

			if (nameToWear == "")
			{
				entity.IOHandler?.QueueOutput("What would you like to wear?");
				return CommandResult.CMD_R_ERRSYNTAX;
			}

			// Search inventory for a match
			var inventoryEntities = DataAccess.GetMany<EntityInanimate>(entity.Inventory?.GetAllEntities(), CacheType.Instance);

			if (inventoryEntities.Count == 0)
			{
				entity.IOHandler?.QueueOutput(nameToWear == "ALL" ? "You don't have anything to wear." : "You don't have that.");
				return CommandResult.CMD_R_FAIL;
			}

			List<EntityInanimate> itemsMarkedAsWorn = new List<EntityInanimate>();

			if (nameToWear == "ALL")
			{
				// A list of items that will be removed from inventory after they have been worn
				List<EntityInanimate> itemsToRemoveFromInventory = new List<EntityInanimate>();

				// Find each item in the inventory that can be worn, and if there is still room in the corresponding slot,
				// equip it.
				foreach (var item in inventoryEntities)
				{
					// Skip items that can't be worn
					if (item.Slot != ItemSlot.None && item.Slot != ItemSlot.None)
					{
						// Get the items equipped at the current item's designated slot
						var itemsEquippedAt = entity.EquippedAt(item.Slot);

						// If there's room, equip the item
						if (itemsEquippedAt.Count < Constants.MAX_EQUIPPED.AT(item.Slot))
						{
							entity.WornEquipment.AddEntity(item.Instance, item);
							itemsMarkedAsWorn.Add(item);
							itemsToRemoveFromInventory.Add(item);
						}
					}
				}

				// Remove items that were equipped from inventory
				foreach (var item in itemsToRemoveFromInventory)
					entity.Inventory.RemoveEntity(item.Instance, item);
			}
			else
			{
				// Find the given item in inventory and equip it, removing any currently equipped items as necessary
				List<EntityInanimate> itemsToRemove = new List<EntityInanimate>();
				EntityInanimate itemMatched = inventoryEntities.FirstOrDefault(item => item.Name.ToUpper().StartsWith(nameToWear));
				if (itemMatched != null)
				{
					// Only equip wearable items
					if (itemMatched.Slot == ItemSlot.None)
					{
						entity.IOHandler?.QueueOutput("You cannot equip that.");
						return CommandResult.CMD_R_FAIL;
					}

					// Get currently equipped items in the matched item's slot
					var equippedItemsInSlot = DataAccess.GetMany<EntityInanimate>(entity.WornEquipment.GetAllEntities(), CacheType.Instance)
						.Where(item => item.Slot == itemMatched.Slot).ToList();

					// Remove an item from the slot if the number of items equipped in it is already at max
					if (equippedItemsInSlot.Count >= Constants.MAX_EQUIPPED.AT(itemMatched.Slot))
					{
						var itemToRemove = equippedItemsInSlot.OrderBy(item => item.Name).ThenBy(item => item.ShortDescription).First();
						entity.WornEquipment.RemoveEntity(itemToRemove.Instance, itemToRemove);
						entity.Inventory.AddEntity(itemToRemove.Instance, itemToRemove);
						entity.IOHandler?.QueueOutput(
							string.Format("You remove {0} and put it in your pack.", itemToRemove.ShortDescription));
					}

					// Equip the item
					itemsMarkedAsWorn.Add(itemMatched);
					entity.WornEquipment.AddEntity(itemMatched.Instance, itemMatched);
					entity.Inventory.RemoveEntity(itemMatched.Instance, itemMatched);
				}
				else
				{
					// The item requested to be removed was not found
					entity.IOHandler.QueueOutput("You don't have that.");
					return CommandResult.CMD_R_FAIL;
				}
			}

			// Reorder list
			itemsMarkedAsWorn = itemsMarkedAsWorn.OrderBy(i => i.Name).ThenBy(i => i.ShortDescription).ToList();

			// Parse output based on items worn
			if (itemsMarkedAsWorn.Count == 0)
			{
				entity.IOHandler?.QueueOutput("There is nothing more for you to wear.");
			}
			else
			{
				// The output for items being worn
				string wearOutput = "";

				foreach (var item in itemsMarkedAsWorn)
				{
					switch (item.Slot)
					{
						case ItemSlot.Light:
							wearOutput = string.Format("You equip {0} as your lightsource.", item.ShortDescription);
							break;
						case ItemSlot.Orbit:
							wearOutput = string.Format("You release {0} in your orbit.", item.ShortDescription);
							break;
						case ItemSlot.OneHandedWeapon:
							wearOutput = string.Format("You equip {0} as your weapon.", item.ShortDescription);
							break;
						case ItemSlot.TwoHandedWeapon:
							wearOutput = string.Format("You equip {0} as your weapon.", item.ShortDescription);
							break;
						case ItemSlot.Shield:
							wearOutput = string.Format("You equip {0} as your shield.", item.ShortDescription);
							break;
						default:
							wearOutput = string.Format("You wear {0} upon your {1}.", item.ShortDescription, item.Slot.ToString().ToLower());
							break;
					}
					entity.IOHandler?.QueueOutput(wearOutput);
				}
			}

			return CommandResult.CMD_R_SUCCESS;
		}
	}
}