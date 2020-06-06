﻿using Hedron.System;

namespace Hedron.Commands.Movement
{
	/// <summary>
	/// Moves the entity east
	/// </summary>
	public class East : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public East()
		{
			FriendlyName = "east";
			HasPriority = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.EAST);
		}
	}
}