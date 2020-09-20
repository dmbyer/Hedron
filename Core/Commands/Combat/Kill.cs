using Hedron.Core.Combat;
using Hedron.Core.Container;
using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Living;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using Hedron.Core.System.Text;
using System.Linq;

namespace Hedron.Core.Commands.Combat
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
            ValidStates.Add(EntityState.Active);
        }

        public override CommandResult Execute(CommandEventArgs commandEventArgs)
        {
            try
            {
                base.Execute(commandEventArgs);
            }
            catch (StateException)
            {
                if (commandEventArgs.Entity.State == EntityState.Combat)
                {
                    return CommandResult.Failure("You are already in combat!");
                }
                else
                {
                    var errMessage = $"Unexpected entity state: {commandEventArgs.Entity.State}.";
                    Logger.Error(nameof(CommandService), nameof(Kill), errMessage);
                    return CommandResult.Failure(errMessage);
                }
            }
            catch (CommandException ex)
            {
                return ex.CommandResult;
            }

            var room = EntityContainer.GetInstanceParent<Room>(commandEventArgs.Entity.Instance);
            var entities = DataAccess.GetMany<EntityAnimate>(room.GetAllEntities<EntityAnimate>(), CacheType.Instance);
            Player player = (Player)commandEventArgs.Entity;
            uint? targetID = null;

            // Find first matching target
            targetID = Parse.MatchOnEntityNameByOrder(commandEventArgs.Argument, entities.Cast<IEntity>().ToList())?.Instance;

            if (targetID == player?.Instance)
            {
                return CommandResult.Failure("You cannot kill yourself.");
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