using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Entity;
using Hedron.System.Exceptions;
using Hedron.System;

namespace Hedron.Commands.Operational
{
	public class Config : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Config()
		{
			FriendlyName = "config";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
			RequiresFullMatch = true;
			ValidStates.Add(Network.GameState.Active);
			ValidStates.Add(Network.GameState.Combat);
		}

		/// <summary>
		/// Sets player configurations
		/// </summary>
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
			string opt = CommandHandler.ParseFirstArgument(CommandHandler.ParseArgument(commandEventArgs.Argument)).ToUpper();

			switch (arg)
			{
				case "AREANAME":
					if (opt == "ON")
					{
						player.Configuration.DisplayAreaName = true;
						output.Append("You will now see area names.");
					}
					else if (opt == "OFF")
					{
						player.Configuration.DisplayAreaName = false;
						output.Append("You will no longer see area names.");
					}
					else
					{
						return CommandResult.InvalidSyntax("config areaname", new List<string> { "on", "off" });
					}
					break;
				default:
					return CommandResult.InvalidSyntax(nameof(Config), new List<string> { "areaname" }, new List<string> { "option" });
			}

			return CommandResult.Success(output.Output);
		}
	}
}