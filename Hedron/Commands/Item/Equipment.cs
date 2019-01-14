using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Item
{
	/// <summary>
	/// Reviews the contents of worn equipment
	/// </summary>
	public class Equipment : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Equipment()
		{
			FriendlyName = "equipment";
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

			var output = new OutputBuilder("Equipment: ");
			var wornItems = commandEventArgs.Entity.GetEquippedItems()
				.OrderBy(item => item.Name)
				.ToList();

			if (wornItems.Count == 0)
			{
				output.Append("You aren't wearing anything.");
			}
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
	}
}
