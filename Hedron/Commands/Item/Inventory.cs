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
	/// Reviews the contents of the inventory
	/// </summary>
	public class Inventory : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Inventory()
		{
			FriendlyName = "inventory";
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

			var output = new OutputBuilder("Inventory: ");
			var entities = commandEventArgs.Entity.GetInventoryItems();

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
	}
}
