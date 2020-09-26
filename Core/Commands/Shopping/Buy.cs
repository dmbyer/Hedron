using Hedron.Core.Container;
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
	/// Reviews the contents of the shop
	/// </summary>
	public class Buy : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Buy()
		{
			FriendlyName = "buy";
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

			var room = commandEventArgs.Entity.GetInstanceParentRoom();
			if (room == null)
				return CommandResult.Failure("You must be in a shop first.");

			if (!room.IsShop)
				return CommandResult.Failure("There is no shop available here.");

			var output = new OutputBuilder();
			var items = room.ShopItems.GetAllEntitiesAsObjects<EntityInanimate>();

			if (items.Count == 0)
			{
				output.Append("There is nothing left to buy!");
			}
			else
			{
				var nameToBuy = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

				if (nameToBuy == "")
					return CommandResult.InvalidSyntax(nameof(Sell), new List<string> { "item name" });

				var itemMatched = Parse.MatchOnEntityNameByOrder(nameToBuy, items.Cast<IEntity>().ToList());

				if (itemMatched == null)
				{
					return CommandResult.Failure("That's not for sale here.");
				}
				var item = (EntityInanimate)itemMatched;


				if (commandEventArgs.Entity.Currency.TotalCopper < item.Value.TotalCopper ||
						commandEventArgs.Entity.Currency.Vita < item.Value.Vita ||
						commandEventArgs.Entity.Currency.Menta < item.Value.Menta ||
						commandEventArgs.Entity.Currency.Astra < item.Value.Astra)
				{
					return CommandResult.Failure($"You don't have enough to pay for that! ({item.Value})");
				}

				output.Append($"You buy {item.ShortDescription} for {item.Value}!");

				// Move item and subtract currency
				room.ShopItems.RemoveEntity(item.Instance, item);
				commandEventArgs.Entity.AddInventoryItem(item.Instance);
				commandEventArgs.Entity.Currency -= item.Value;
			}

			return CommandResult.Success(output.Output);
		}
	}
}
