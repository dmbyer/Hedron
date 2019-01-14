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

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		public StateException(string message)
			: base(message)
		{
			CommandResult = CommandResult.Failure(message);
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public StateException(string message, Exception inner)
			: base(message, inner)
		{
			CommandResult = CommandResult.Failure(message);
		}
	}
}