using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;

namespace Hedron.Core.Commands.Item
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
			ValidStates.Add(EntityState.Active);
			ValidStates.Add(EntityState.Combat);
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
				output.Append(Formatter.NewTableFromList(itemDescriptions, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}
	}
}
