using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.System;

namespace Hedron.Commands.Movement
{
	/// <summary>
	/// Moves the entity down
	/// </summary>
	public class Down : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Down()
		{
			FriendlyName = "down";
			HasPriority = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.DOWN);
		}
	}
}