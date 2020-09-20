using Hedron.Core.Entities.Properties;
using Hedron.Core.System;

namespace Hedron.Core.Commands.Movement
{
	/// <summary>
	/// Moves the entity south
	/// </summary>
	public class South : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public South()
		{
			FriendlyName = "south";
			HasPriority = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(EntityState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.SOUTH);
		}
	}
}