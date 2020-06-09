using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;

namespace Hedron.Commands.Operational
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
			ValidStates.Add(Network.GameState.Active);
			ValidStates.Add(Network.GameState.Combat);
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
					CommandHandler.AvailableCommands(
						commandEventArgs.PrivilegeOverride == null
							? commandEventArgs.Entity.PrivilegeLevel
							: (PrivilegeLevel)commandEventArgs.PrivilegeOverride),
						6, 5, Formatter.DefaultIndent));

			return CommandResult.Success(output.Output);
		}
	}
}