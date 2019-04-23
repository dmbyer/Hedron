using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Entity;
using Hedron.Core.Property;
using Hedron.System;
using Hedron.System.Exceptions;
using Hedron.System.Text;

namespace Hedron.Commands.Item
{
	/// <summary>
	/// Wears an object
	/// </summary>
	public class Wear : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Wear()
		{
			FriendlyName = "wear";
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
			ValidStates.Add(Network.GameState.Combat);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			var nameToWear = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();
			var entity = commandEventArgs.Entity;

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
				var itemMatched = Parse.MatchOnEntityNameByOrder(nameToWear, inventoryEntities.Cast<IEntity>().ToList());
				if (itemMatched != null)
				{
					try
					{
						// Equip the item and swap already equipped items to inventory
						itemsRemoved = entity.EquipItemAt(itemMatched.Instance, ((EntityInanimate)itemMatched).Slot, true);

						entity.RemoveInventoryItem(itemMatched.Instance);
						itemsMarkedAsWorn.Add((EntityInanimate)itemMatched);

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
