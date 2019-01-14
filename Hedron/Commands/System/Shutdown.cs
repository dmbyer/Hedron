using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.System
{
	/// <summary>
	/// Flags the server to shutdown
	/// </summary>
	public class Shutdown : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Shutdown()
		{
			FriendlyName = "shutdown";
			PrivilegeLevel = PrivilegeLevel.Administrator;
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

			World.Shutdown = true;
			Logger.Info(nameof(CommandHandler), nameof(Shutdown), $"{commandEventArgs.Entity.Name} [Prototype {commandEventArgs.Entity.Prototype}] requested server shutdown.");

			return CommandResult.Success("Shutting down the server.");
		}
	}
}