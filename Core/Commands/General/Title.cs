using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.General
{
    /// <summary>
    /// Modifies the player's short description / title
    /// </summary>
    public class Title : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Title()
		{
			FriendlyName = "title";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
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

			var player = (Player)commandEventArgs.Entity;
			var output = new OutputBuilder();

			player.ShortDescription = commandEventArgs.Argument;
			output.Append($"Your short description has been set to:\n   {commandEventArgs.Argument}");

			return CommandResult.Success(output.Output);
		}
	}
}
