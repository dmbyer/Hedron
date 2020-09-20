using Hedron.Core.Entities.Properties;
using Hedron.Core.System;

namespace Hedron.Core.Commands.Movement
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
			ValidStates.Add(EntityState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			return new MoveEntity().Execute(commandEventArgs, Constants.EXIT.DOWN);
		}
	}
}