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
				return CommandResult.CMD_R_FAIL;
			}

			/// Player-only command to sign out.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }
			
			Player player = (Player)entity;

			string arg = ParseFirstArgument(argument).ToUpper();
			string opt = ParseFirstArgument(ParseArgument(argument)).ToUpper();
			
			switch (arg)
			{
				case "AREANAME":
					if (opt == "ON")
					{
						player.Configuration.DisplayAreaName = true;
						player.IOHandler.QueueOutput("You will now see area names.");
					}
					else if (opt == "OFF")
					{
						player.Configuration.DisplayAreaName = false;
						player.IOHandler.QueueOutput("You will no longer see area names.");
					}
					else
					{
						player.IOHandler.QueueOutput("Invalid option. Use config areaname <on/off>.");
						return CommandResult.CMD_R_ERRSYNTAX;
					}
					break;
				default:
					player.IOHandler.QueueOutput("Invalid syntax. Use config <category> <option>. Valid categories are: areaname.");
					return CommandResult.CMD_R_ERRSYNTAX;
			}

			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			/// Player-only command to sign out.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			entity.IOHandler.QueueOutput("Goodbye!");
			return CommandResult.CMD_R_QUIT;
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
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to view Help.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			entity.IOHandler.QueueOutput("Valid commands are:");
			entity.IOHandler.QueueOutput("help\nquit\ntest\nnorth\neast\nsouth\nwest\nup\ndown\nlook\ngoto");
			return CommandResult.CMD_R_SUCCESS;
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
				return CommandResult.CMD_R_FAIL;
			}

			/// Player-only command to Save.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			/*
            try
            {
                SavePlayer(cPlayer);
                entity.IOHandler.QueueOutput("Saved!");
                return CommandResult.CMD_R_SUCCESS;
            }
            catch (exception)
	        {
                entity.IOHandler.QueueOutput(ex.what());
                return CommandResult.CMD_R_FAIL;
            }
            */
			return CommandResult.CMD_R_SUCCESS;
		}
	}
}