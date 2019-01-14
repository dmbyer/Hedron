using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Commands;

namespace Hedron.System.Exceptions
{
	/// <summary>
	/// For situations in which the privilege level is not high enough to execute an action
	/// </summary>
	public class PrivilegeException : CommandException
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public PrivilegeException()
		{
			CommandResult = CommandResult.Failure("Error: Invalid privilege level for this command.");
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		public PrivilegeException(string message)
			: base(message)
		{
			CommandResult = CommandResult.Failure(message);
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public PrivilegeException(string message, Exception inner)
			: base(message, inner)
		{
			CommandResult = CommandResult.Failure(message);
		}
	}
}