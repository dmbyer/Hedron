using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Entity;
using Hedron.System;
using Hedron.System.Exceptions;
using Hedron.System.Text;

namespace Hedron.Commands.General
{
	/// <summary>
	/// Modifies the player's prompt
	/// </summary>
	public class Prompt : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Prompt()
		{
			FriendlyName = "prompt";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
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

			var player = (Player)commandEventArgs.Entity;
			var output = new OutputBuilder();
			var argument = CommandHandler.ParseFirstArgument(commandEventArgs.Argument).ToUpper();

			switch (argument)
			{
				case "CLEAR":
					player.Prompt = "";
					output.Append("Your prompt has been cleared.");
					break;
				case "RESET":
					if (player.Configuration.UseColor)
						player.Prompt = Constants.Prompt.DEFAULT_COLOR;
					else
						player.Prompt = Constants.Prompt.DEFAULT;

					output.Append($"Your prompt has been set to:\n   {player.Prompt}");

					break;
				case "SET":
					var arg = CommandHandler.ParseArgument(commandEventArgs.Argument);
					if (arg == "")
					{
						var help = Formatter.NewTableFromList(
							new List<string>
							{
								"Valid placeholders are:",
								"Valid color codes are:",
								"$hp - Current hit points",
								$"`{Formatter.FriendlyColorBlack} - Black",
								"$HP - Maximum hit points",
								$"`{Formatter.FriendlyColorBlue} - Blue",
								"$st - Current stamina",
								$"`{Formatter.FriendlyColorBold} - (Bold)",
								"$ST - Maximum stamina",
								$"`{Formatter.FriendlyColorCyan} - Cyan",
								"$en - Current energy",
								$"`{Formatter.FriendlyColorGreen} - Green",
								"$EN - Maximum energy",
								$"`{Formatter.FriendlyColorMagenta} - Magenta",
								"",
								$"`{Formatter.FriendlyColorRed} - Red",
								"",
								$"`{Formatter.FriendlyColorReset} - (Reset)",
								"",
								$"`{Formatter.FriendlyColorWhite} - White",
								"",
								$"`{Formatter.FriendlyColorYellow} - Yellow"
							}, 2, 4, 4);

						if (!player.Prompt.EndsWith(Formatter.FriendlyColorReset))
							player.Prompt += Formatter.FriendlyColorReset;

						return CommandResult.InvalidSyntax(nameof(Prompt), $"\n{help}", new List<string> { "set [new prompt]" });
					}

					player.Prompt = arg;
					output.Append($"Your prompt has been set to:\n   {arg}");
					break;
				default:
					return CommandResult.InvalidSyntax(nameof(Prompt), new List<string> { "clear", "reset", "set" });
			}

			return CommandResult.Success(output.Output);
		}
	}
}
