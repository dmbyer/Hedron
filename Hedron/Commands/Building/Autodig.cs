using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Building
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

			var output = new OutputBuilder();
			Player player = (Player)commandEventArgs.Entity;
			string arg = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

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
