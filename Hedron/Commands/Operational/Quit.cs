﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Operational
{
	/// <summary>
	/// Quits the game
	/// </summary>
	public class Quit : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Quit()
		{
			FriendlyName = "quit";
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

			return new CommandResult(ResultCode.QUIT, "Goodbye!");
		}
	}
}