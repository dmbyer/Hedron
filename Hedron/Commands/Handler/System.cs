using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;

namespace Hedron.Commands
{
	public static partial class CommandHandler
	{
		/// <summary>
		/// Shuts the server down
		/// </summary>
		private static CommandResult Shutdown(string argument, EntityAnimate entity)
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

			// Player-only command to shut the server down.
			if (!Guard.IsPlayer(entity)) { return CommandResult.PlayerOnly(); }

			// TODO: Check privilege level

			World.Shutdown = true;
			Logger.Info(nameof(CommandHandler), nameof(Shutdown), $"{entity.Name} [Prototype {entity.Prototype}] requested server shutdown.");

			return CommandResult.Success("Shutting down the server.");
		}
	}
}