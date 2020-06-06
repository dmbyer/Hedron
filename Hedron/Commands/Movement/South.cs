using Hedron.System;

namespace Hedron.Commands.Movement
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
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.SOUTH);
		}
	}
}