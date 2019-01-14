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