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
				return CommandResult.NullEntity();
			}

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var output = new OutputBuilder();

			Player player = (Player)entity;

			string arg = ParseFirstArgument(argument).ToUpper();

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

		/// <summary>
		/// Lists entities
		/// </summary>
		private static CommandResult EList(string argument, EntityAnimate entity)
		{
			try
			{
				Guard.ThrowIfNull(entity, nameof(entity));
			}
			catch (ArgumentNullException ex)
			{
				Logger.Error(nameof(CommandHandler), nameof(Prompt), ex.Message);
				return CommandResult.NullEntity();
			}

			// Player-only command to list mobs.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }
			
			return CommandResult.NotImplemented(nameof(EList));
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
				return CommandResult.NullEntity();
			}

			var output = new OutputBuilder();

			// Player-only command to get/set prompt.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			var parentRoom = EntityContainer.GetInstanceParent<Room>(entity.Instance);

			if (parentRoom == null)
			{
				output.Append("You must be in a room to use this command.");
				return CommandResult.Failure(output.Output);
			}

			var arg = ParseFirstArgument(argument).ToUpper();
			var txt = ParseArgument(argument);

			switch(arg)
			{
				case "NAME":
					parentRoom.Name = txt;
					if (txt != "")
						output.Append($"The room name has been set to {txt}.");
					else
						output.Append("The room name has been cleared.");
					break;
				case "DESC":
					parentRoom.Description = txt;
					if (txt != "")
						output.Append("The room description has been set.");
					else
						output.Append("The room description has been cleared.");
					break;
				default:
					return CommandResult.InvalidSyntax(nameof(Set), new List<string> { "name", "desc" }, new List<string> { "new text" });
			}

			// Update prototype and persist changes
			parentRoom.CopyTo(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));
			DataPersistence.SaveObject(DataAccess.Get<Room>(parentRoom.Prototype, CacheType.Prototype));

			return CommandResult.Success(output.Output);
		}
	}
}