using Hedron.Core.Commands;
using Hedron.Core.Combat;
using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Linq;

namespace Hedron.Core.Skills.Active
{
    public class Kick : ActiveSkill
    {
        public Kick()
        {
            FriendlyName = "kick";
            HasPriority = false;
            PrivilegeLevel = PrivilegeLevel.NPC;
            ValidStates.Add(EntityState.Active);
            ValidStates.Add(EntityState.Combat);
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
				var errMessage = $"Unexpected entity state: {commandEventArgs.Entity.State}.";
				Logger.Error(nameof(CommandService), nameof(Kick), errMessage);
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

			if (commandEventArgs.Entity.StateHandler.State == PlayerState.Combat)
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
 