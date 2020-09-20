using Hedron.Core.Entities.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;

namespace Hedron.Core.Commands.Operational
{
    /// <summary>
    /// Shows help files
    /// </summary>
    public class Help : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Help()
		{
			FriendlyName = "help";
			RequiresFullMatch = true;
			ValidStates.Add(EntityState.Active);
			ValidStates.Add(EntityState.Combat);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			// TODO: Update once command dictionary is created to iterate Keys and output valid commands.
			// TODO: Create help file for each command.
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			var output = new OutputBuilder();

			output.Append("Valid commands are:");

			output.Append(
				Formatter.NewTableFromList(
					CommandService.AvailableCommands(
						commandEventArgs.PrivilegeOverride == null
							? commandEventArgs.Entity.PrivilegeLevel
							: (PrivilegeLevel)commandEventArgs.PrivilegeOverride),
						6, 5, Formatter.DefaultIndent));

			return CommandResult.Success(output.Output);
		}
	}
}