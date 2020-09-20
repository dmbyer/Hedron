using Hedron.Core.Commands;

namespace Hedron.Core.System.Exceptions.Command
{
	/// <summary>
	/// For situations in which the state is incorrect for executing a certain action
	/// </summary>
	public class StateException : CommandException
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public StateException()
		{
			CommandResult = CommandResult.Failure("Error: Invalid state for this command.");
		}
	}
}