using Hedron.Core.Container;
using Hedron.Core.Locale;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;

namespace Hedron.Core.Commands.Shopping
{
	/// <summary>
	/// Reviews the contents of the shop
	/// </summary>
	public class Shop : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Shop()
		{
			FriendlyName = "shop";
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
			var items = room.GetShopItems<EntityInanimate>();

			if (items.Count == 0)
			{
				output.Append("Everything has been purchased!");
			}
			else
			{
				output.Append("Available for purchase: ");

				// var itemDescriptions = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(items, EntityQuantityMapper.MapStringTypes.ShortDescription);
				var itemDescriptions = new List<string>();

				foreach (var i in items)
				{
					itemDescriptions.Add(i.Value.ToString());
					itemDescriptions.Add(i.ShortDescription);
				}
				output.Append(Formatter.NewTableFromList(itemDescriptions, 2, 4, 4));
			}

			return CommandResult.Success(output.Output);
		}
	}
}
