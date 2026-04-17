using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Core.Modules.Locale;
using Core.ECS.Properties;

namespace Hedron.Core.Commands.System
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

			World.Shutdown = true;
			Logger.Info(nameof(CommandService), nameof(Shutdown), $"{commandEventArgs.Entity.Name} [Prototype {commandEventArgs.Entity.Prototype}] requested server shutdown.");

			return CommandResult.Success("Shutting down the server.");
		}
	}
}