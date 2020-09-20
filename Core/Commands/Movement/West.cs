using Hedron.Core.Entities.Properties;
using Hedron.Core.System;

namespace Hedron.Core.Commands.Movement
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
			ValidStates.Add(EntityState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.WEST);
		}
	}
}