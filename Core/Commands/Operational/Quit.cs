using Hedron.Core.Entities.Properties;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.Operational
{
	/// <summary>
	/// Quits the game
	/// </summary>
	public class Quit : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Quit()
		{
			FriendlyName = "quit";
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

			return new CommandResult(ResultCode.QUIT, "Goodbye!");
		}
	}
}