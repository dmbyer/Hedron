using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.Entities.Properties;

namespace Hedron.Core.Commands.Building
{
    /// <summary>
    /// Lists entities
    /// </summary>
    public class Elist : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Elist()
		{
			FriendlyName = "elist";
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

			return CommandResult.NotImplemented(nameof(Elist));
		}
	}
}