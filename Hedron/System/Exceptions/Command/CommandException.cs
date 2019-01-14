using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Commands;

namespace Hedron.System.Exceptions
{
	public class CommandException : Exception
	{
		/// <summary>
		/// The command result due to the exception
		/// </summary>
		public CommandResult CommandResult { get; protected set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CommandException()
		{
			CommandResult = CommandResult.Failure("Error: General command execution failure.");
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		public CommandException(string message)
			: base(message)
		{
			CommandResult = CommandResult.Failure(message);
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public CommandException(string message, Exception inner)
			: base(message, inner)
		{
			CommandResult = CommandResult.Failure(message);
		}
	}
}