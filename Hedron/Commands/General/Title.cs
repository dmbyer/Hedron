using Hedron.Core.Entity.Living;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Collections.Generic;

namespace Hedron.Commands.General
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
			ValidStates.Add(Network.GameState.Active);
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
