using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Locale;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Commands.Item
{
    /// <summary>
    /// Drops an object to a room
    /// </summary>
    public class Drop : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Drop()
		{
			FriendlyName = "drop";
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

			var nameToDrop = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToDrop == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to drop?");

			if (EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance) == null)
				return CommandResult.Failure("There is nowhere to drop it to.");

			// Search inventory for a match
			var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
			var inventoryEntities = commandEventArgs.Entity.GetInventoryItems();

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
				var itemMatched = Parse.MatchOnEntityNameByOrder(nameToDrop, inventoryEntities.Cast<IEntity>().ToList());
				if (itemMatched != null)
					matchedItems.Add((EntityInanimate)itemMatched);
			}

			if (matchedItems.Count == 0)
				return CommandResult.Failure("You don't have that.");

			foreach (var item in matchedItems)
			{
				commandEventArgs.Entity.RemoveInventoryItem(item.Instance);
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
				output.Append(Formatter.NewTableFromList(droppedItems, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}
	}
}
