using Hedron.System;

namespace Hedron.Commands.Movement
{
	/// <summary>
	/// Moves the entity west
	/// </summary>
	public class West : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public West()
		{
			FriendlyName = "west";
			HasPriority = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.WEST);
		}
	}
}