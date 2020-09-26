using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands.Item
{
    /// <summary>
    /// Gets an object from a container
    /// </summary>
    public class Get : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Get()
		{
			FriendlyName = "get";
			PrivilegeLevel = PrivilegeLevel.NPC;
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

			var nameToGet = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToGet == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to get?");

			// Search room for a match
			var room = commandEventArgs.Entity.GetInstanceParentRoom();
			var roomEntities = room.Items.GetAllEntitiesAsObjects<EntityInanimate>();

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
				var itemMatched = Parse.MatchOnEntityNameByOrder(nameToGet, roomEntities.Cast<IEntity>().ToList());
				if (itemMatched != null)
					matchedItems.Add((EntityInanimate)itemMatched);
			}

			if (matchedItems.Count == 0)
				return new CommandResult(ResultCode.FAIL, "You don't see that.");

			foreach (var item in matchedItems)
			{
				room.Items.RemoveEntity(item.Instance, item);
				commandEventArgs.Entity.AddInventoryItem(item.Instance);
			}

			if (matchedItems.Count == 1)
			{
				output.Append("You pick up " + matchedItems[0].ShortDescription + ".");
			}
			else
			{
				var itemsPicked = EntityQuantityMapper.ParseEntityQuantitiesAsStrings(matchedItems, EntityQuantityMapper.MapStringTypes.ShortDescription);

				output.Append("You pick up:");
				output.Append(Formatter.NewTableFromList(itemsPicked, 1, 4, 0));
			}

			return CommandResult.Success(output.Output);
		}
	}
}
