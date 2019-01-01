using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Quits the game
		/// </summary>
		private static CommandResult Config(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Quit), ex.Message);
				return CommandResult.NullEntity();
			}

			// Player-only command to sign out.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var output = new OutputBuilder();
			
			Player player = (Player)entity;

			string arg = ParseFirstArgument(argument).ToUpper();
			string opt = ParseFirstArgument(ParseArgument(argument)).ToUpper();
			
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

		/// <summary>
		/// Shows help files
		/// </summary>
		private static CommandResult Help(string argument, EntityAnimate entity)
		{
			// TODO: Update once command dictionary is created to iterate Keys and output valid commands.
			// TODO: Create help file for each command.
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Help), ex.Message);
				return CommandResult.NullEntity();
			}

			// Player-only command to view Help.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var output = new OutputBuilder();

			output.Append("Valid commands are:");
			output.Append(TextFormatter.NewTableFromList(_commandMap.Keys.Where(k => k != "\n").ToList(), 6, 5, TextFormatter.DefaultIndent));
			return CommandResult.Success(output.Output);
		}

		/// <summary>
		/// Quits the game
		/// </summary>
		private static CommandResult Quit(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Quit), ex.Message);
				return CommandResult.NullEntity();
			}

			/// Player-only command to quit.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			return new CommandResult(ResultCode.QUIT, "Goodbye!");
		}

		/// <summary>
		/// Saves player data
		/// </summary>
		private static CommandResult Save(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Save), ex.Message);
				return CommandResult.NullEntity();
			}

			// Player-only command to Save.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }
			
			return CommandResult.NotImplemented(nameof(Save));
		}
	}
}