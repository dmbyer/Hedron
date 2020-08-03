using Hedron.Commands;
using Hedron.Combat;
using Hedron.Core.Container;
using Hedron.Core.Entity.Base;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using Hedron.System.Text;
using System.Linq;

namespace Hedron.Skills.Active
{
    public class Kick : ActiveSkill
    {
        public Kick()
        {
            FriendlyName = "kick";
            HasPriority = false;
            PrivilegeLevel = PrivilegeLevel.NPC;
            ValidStates.Add(Network.GameState.Active);
            ValidStates.Add(Network.GameState.Combat);
			IsCombatCommand = true;
            Cooldown = 1;
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (StateException)
			{
				var errMessage = $"Unexpected entity state: {commandEventArgs.Entity.StateHandler.State}.";
				Logger.Error(nameof(CommandHandler), nameof(Kick), errMessage);
				return CommandResult.Failure(errMessage);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			/* IN PROGRESS
			 * if there's no argument and the entity is in combat, make the target the entity's combat target
			 * if there's an argument, get the matching entity
			 * if the entity is in combat, queue the command
			 * if the entity is not in combat, enter combat and queue the command
			 * This logic might get reused and should maybe be moved up into CombatHandler or something
			 */


			/* Copied from "kill" command, began modifying.
			uint? targetID = null;

			if (commandEventArgs.Entity.StateHandler.State == Network.GameState.Combat)
            {
				targetID = CombatHandler.GetTarget(commandEventArgs.Entity.Instance);
            }

			if (commandEventArgs.Argument != "")
			{
				var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
				var entities = DataAccess.GetMany<EntityAnimate>(room.GetAllEntities<EntityAnimate>(), CacheType.Instance);

				// Find first matching target
				targetID = Parse.MatchOnEntityNameByOrder(commandEventArgs.Argument, entities.Cast<IEntity>().ToList())?.Instance;
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

			*/
			return CommandResult.Success($"You kick!");
		}
	}
}
 