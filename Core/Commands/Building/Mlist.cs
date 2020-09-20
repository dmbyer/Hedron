using Hedron.Core.Entities.Properties;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.Building
{
	/// <summary>
	/// Lists entities
	/// </summary>
	public class Mlist : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Mlist()
		{
			FriendlyName = "mlist";
			PrivilegeLevel = PrivilegeLevel.Builder;
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

			return CommandResult.NotImplemented(nameof(Mlist));
		}
	}
}