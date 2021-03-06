﻿using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands.Item
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

			var nameToDrop = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToDrop == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to drop?");

			var room = commandEventArgs.Entity.GetInstanceParentRoom();

			if (room == null)
				return CommandResult.Failure("There is nowhere to drop it to.");

			// Search inventory for a match
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
				room.Items.AddEntity(item.Instance, item, false);
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
