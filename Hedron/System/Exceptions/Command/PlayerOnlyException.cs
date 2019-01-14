using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Commands;

namespace Hedron.System.Exceptions
{
	/// <summary>
	/// For situations where only a player may execute a command due to casting requirements
	/// </summary>
	public class PlayerOnlyException : CommandException
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public PlayerOnlyException()
		{
			CommandResult = CommandResult.Failure("Error: Player-only command.");
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		public PlayerOnlyException(string message)
			: base(message)
		{
			CommandResult = CommandResult.Failure(message);
		}

		/// <summary>
		/// New CommandException
		/// </summary>
		/// <param name="message">The exception message</param>
		/// <param name="inner">The inner exception</param>
		public PlayerOnlyException(string message, Exception inner)
			: base(message, inner)
		{
			CommandResult = CommandResult.Failure(message);
		}
	}
}
