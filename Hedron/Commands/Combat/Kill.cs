using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Combat;
using Hedron.Core;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Combat
{
	/// <summary>
	/// Initiates combat
	/// </summary>
	public class Kill : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Kill()
		{
			FriendlyName = "kill";
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (StateException)
			{
				if (commandEventArgs.Entity.StateHandler.State == Network.GameState.Combat)
				{
					return CommandResult.Failure("You are already in combat!");
				}
				else
				{
					var errMessage = $"Unexpected entity state: {commandEventArgs.Entity.StateHandler.State}.";
					Logger.Error(nameof(CommandHandler), nameof(Kill), errMessage);
					return CommandResult.Failure(errMessage);
				}
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
			var entities = DataAccess.GetMany<EntityAnimate>(room.GetAllEntities<EntityAnimate>(), CacheType.Instance);
			uint? targetID = null;

			// Find first matching target
			foreach (var ent in entities)
			{
				if (ent.Instance != targetID && ent.Name.StartsWith(commandEventArgs.Argument))
				{
					targetID = ent.Instance;
					break;
				}
			}

			if (targetID != null)
			{
				CombatHandler.Enter(commandEventArgs.Entity.Instance, targetID, false);
			}
			else
			{
				return CommandResult.Failure("There is no such target.");
			}

			var targetName = DataAccess.Get<EntityAnimate>(targetID, CacheType.Instance).ShortDescription;

			return CommandResult.Success($"You attack {targetName}!");
		}
	}
}
