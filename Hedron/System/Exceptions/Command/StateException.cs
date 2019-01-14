using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Commands;

namespace Hedron.System.Exceptions
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