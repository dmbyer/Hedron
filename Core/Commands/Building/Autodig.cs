using Hedron.Core.Entities.Living;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using System.Collections.Generic;
using Hedron.Core.Entities.Properties;

namespace Hedron.Core.Commands.Building
{
    /// <summary>
    /// Enables automatic room creation if room does not exist when moving directions
    /// </summary>
    public class Autodig : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Autodig()
		{
			FriendlyName = "autodig";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Builder;
			RequiresFullMatch = true;
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

			var output = new OutputBuilder();
			Player player = (Player)commandEventArgs.Entity;
			string arg = CommandService.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			switch (arg)
			{
				case "ON":
					player.Configuration.Autodig = true;
					output.Append("You will now automatically dig when moving.");
					break;
				case "OFF":
					player.Configuration.Autodig = false;
					output.Append("You will no longer automatically dig when moving.");
					break;
				case "":
					string configState = player.Configuration.Autodig == true ? "on" : "off";
					output.Append($"Autodig is set to {configState}.");
					break;
				default:
					return CommandResult.InvalidSyntax(nameof(Autodig), new List<string> { "on", "off" });
			}

			return CommandResult.Success(output.Output);
		}
	}
}
