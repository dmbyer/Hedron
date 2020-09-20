using Hedron.Core.Entities.Properties;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.Operational
{
	/// <summary>
	/// Saves player data
	/// </summary>
	public class Save : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Save()
		{
			FriendlyName = "save";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
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

			return CommandResult.NotImplemented(nameof(Save));
		}
	}
}
