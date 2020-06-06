using Hedron.System;

namespace Hedron.Commands.Movement
{
	/// <summary>
	/// Moves the entity up
	/// </summary>
	public class Up : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Up()
		{
			FriendlyName = "up";
			HasPriority = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.UP);
		}
	}
}