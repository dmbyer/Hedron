using Hedron.Core.Entities.Properties;
using Hedron.Core.System;

namespace Hedron.Core.Commands.Movement
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
			ValidStates.Add(EntityState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.EAST);
		}
	}
}