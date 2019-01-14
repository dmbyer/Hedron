using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Operational
{
	/// <summary>
	/// Saves player data
	/// </summary>
	public class Save : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Save()
		{
			FriendlyName = "save";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
			RequiresFullMatch = true;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			return CommandResult.NotImplemented(nameof(Save));
		}
	}
}
