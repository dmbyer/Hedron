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
			CommandResult = CommandResult.PrivilegeLevel("Error: Invalid privilege level for this command.");
		}
	}
}