using Hedron.Core.Entity.Base;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Commands.Item
{
    /// <summary>
    /// Removes an object
    /// </summary>
    public class Remove : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Remove()
		{
			FriendlyName = "remove";
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

			var nameToRemove = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			if (nameToRemove == "")
				return new CommandResult(ResultCode.ERR_SYNTAX, "What would you like to remove?");

			// Search equipment for a match
			var equipmentEntities = commandEventArgs.Entity.GetEquippedItems();

			if (equipmentEntities.Count == 0)
			{
				if (nameToRemove == "ALL")
					return CommandResult.Failure("You aren't wearing anything.");
				else
					return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that equipped.");
			}

			List<EntityInanimate> matchedItems;

			if (nameToRemove == "ALL")
				matchedItems = equipmentEntities;
			else
			{
				matchedItems = new List<EntityInanimate>();
				var itemMatched = Parse.MatchOnEntityNameByOrder(nameToRemove, equipmentEntities.Cast<IEntity>().ToList());
				if (itemMatched != null)
					matchedItems.Add((EntityInanimate)itemMatched);
			}

			if (matchedItems.Count == 0)
				return new CommandResult(ResultCode.ERR_SYNTAX, "You don't have that equipped.");

			var output = new OutputBuilder();

			foreach (var item in matchedItems)
			{
				commandEventArgs.Entity.UnequipItem(item.Instance);
				output.Append($"You remove {item.ShortDescription} and place it in your pack.");
			}

			return CommandResult.Success(output.Output);
		}
	}
}
