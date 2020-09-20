using Hedron.Core.Container;
using Hedron.Data;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands.Shopping
{
	/// <summary>
	/// Sells an item to a shop
	/// </summary>
	public class Sell : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Sell()
		{
			FriendlyName = "sell";
			ValidStates.Add(EntityState.Active);
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

			var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
			if (room == null)
				return CommandResult.Failure("You must be in a shop first.");

			if (!room.IsShop)
				return CommandResult.Failure("There is no shop available here.");

			var output = new OutputBuilder();
			var nameToSell = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToSell == "")
				return CommandResult.InvalidSyntax(nameof(Sell), new List<string> { "item name" });

			// Search inventory for a match
			var inventoryEntities = commandEventArgs.Entity.GetInventoryItems();

			if (inventoryEntities.Count == 0)
			{
				return new CommandResult(ResultCode.FAIL, "You don't have anything to sell!");
			}

			var itemMatched = Parse.MatchOnEntityNameByOrder(nameToSell, inventoryEntities.Cast<IEntity>().ToList());

			if (itemMatched == null)
			{
				return CommandResult.Failure("You can't seem to find it.");
			}

			var item = (EntityInanimate)itemMatched;
			output.Append($"You sell {item.ShortDescription} for {item.Value}!");

			// Remove item and give currency
			commandEventArgs.Entity.RemoveInventoryItem(item.Instance);
			commandEventArgs.Entity.Currency += item.Value;

			DataAccess.Remove<EntityInanimate>(item.Instance, CacheType.Instance);

			return CommandResult.Success(output.Output);
		}
	}
}
