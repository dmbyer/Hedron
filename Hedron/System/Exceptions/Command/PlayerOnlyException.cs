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
			CommandResult = CommandResult.PlayerOnly();
		}
	}
}