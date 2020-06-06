using Hedron.Commands;
using System;

namespace Hedron.System.Exceptions.Command
{
	public class CommandException : Exception
	{
		/// <summary>
		/// The command result due to the exception
		/// </summary>
		public CommandResult CommandResult { get; protected set; }

		/// <summary>
		/// Private default constructor
		/// </summary>
		protected CommandException()
		{

		}

		/// <summary>
		/// Default constructor
		/// </summary>
		public CommandException(CommandResult commandResult)
		{
			CommandResult = commandResult;
		}
	}
}