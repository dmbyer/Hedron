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
		/// Enables automatic room creation if room does not exist when moving directions
		/// </summary>
		private static CommandResult Autodig(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			Player player = (Player)entity;

			string arg = ParseFirstArgument(argument).ToUpper();

			switch (arg)
			{
				case "ON":
					player.Configuration.Autodig = true;
					player.IOHandler.QueueOutput("You will now automatically dig when moving.");
					break;
				case "OFF":
					player.Configuration.Autodig = false;
					player.IOHandler.QueueOutput("You will no longer automatically dig when moving.");
					break;
				case "":
					string configState = player.Configuration.Autodig == true ? "on" : "off";
					player.IOHandler.QueueOutput($"Autodig is set to {configState}.");
					break;
				default:
					player.IOHandler.QueueOutput("Invalid syntax. Use autodig on/off.");
					return CommandResult.CMD_R_ERRSYNTAX;
			}

			return CommandResult.CMD_R_SUCCESS;
		}

		/// <summary>
		/// Sets properties of things for building
		/// </summary>
		private static CommandResult Set(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.CMD_R_FAIL;
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return PlayerOnlyCommand(); }

			var parentRoom = DataAccess.Get<Room>(entity.Parent, CacheType.Instance);

			if (parentRoom == null)
			{
				entity.IOHandler.QueueOutput("You must be in a room to use this command.");
				return CommandResult.CMD_R_FAIL;
			}

			var arg = ParseFirstArgument(argument).ToUpper();
			var txt = ParseArgument(argument);

			switch(arg)
			{
				case "NAME":
					parentRoom.Name = txt;
					if (txt != "")
						entity.IOHandler.QueueOutput($"The room name has been set to {txt}.");
					else
						entity.IOHandler.QueueOutput("The room name has been cleared.");
					break;
				case "DESC":
					parentRoom.Description = txt;
					if (txt != "")
						entity.IOHandler.QueueOutput("The room description has been set.");
					else
						entity.IOHandler.QueueOutput("The room description has been cleared.");
					break;
				default:
					entity.IOHandler.QueueOutput("Invalid syntax. Use set <name/desc> <new text>.");
					return CommandResult.CMD_R_ERRSYNTAX;
			}

			// Update prototype and persist changes
			parentRoom.CopyTo(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));
			DataPersistence.SaveObject(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));

			return CommandResult.CMD_R_SUCCESS;
		}
	}
}