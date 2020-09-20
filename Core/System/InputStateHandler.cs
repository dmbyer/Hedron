using Hedron.Core.Commands;
using Hedron.Core.Commands.Movement;
using Hedron.Core.Entities.Base;
using Hedron.Core.Locale;
using Hedron.Data;
using System.Collections.Generic;
using Hedron.Core.Entities.Properties;

namespace Hedron.Core.System
{
    /// <summary>
    /// Handles input from player based on game state
    /// </summary>
    public class StateHandler
    {
        public StateHandler()
        {
            
        }

        /// <summary>
        /// Process network input based on state
        /// </summary>
        /// <param name="input">The input data</param>
        /// <param name="entity">The data's corresponding entity</param>
        /// <returns></returns>
        public CommandResult ProcessInput(string input, EntityAnimate entity)
        {
            if (entity == null)
                return CommandResult.NullEntity();

            switch (entity.State)
            {
                case EntityState.NameSelection:
                    CommandResult result = HandleNameSelection(input, entity);

                    if (result.ResultCode == ResultCode.SUCCESS)
                    {
                        entity.IOHandler?.QueueRawOutput(result.ResultMessage);
                        var startingRooms = DataAccess.GetInstancesOfPrototype<Room>(DataAccess.Get<World>(0, CacheType.Instance).StartingLocation);

                        var args = new CommandEventArgs(
                            startingRooms.Count > 0 ? startingRooms[0].Instance.ToString() : "0",
                            entity,
                            null);

                        entity.State = EntityState.Active;
                        result = new Goto().Execute(args);
                    }

                    return result;

                case EntityState.Active:
                    return CommandService.ProcessCommand(input, entity, new List<EntityState> { EntityState.Active });

                case EntityState.Combat:
                    // TODO: Leverage command queue instead
                    var processCommands = CommandService.ProcessCommand(input, entity, new List<EntityState> { EntityState.Combat });
                    
                    if (processCommands.ResultCode == ResultCode.FAIL)
                    {
                        return HandleCombatInput(input, entity);
                    }
                    else
                    {
                        return processCommands;
                    }

                default:
                    return CommandResult.Failure("Invalid entity state.");
            }
        }

        /// <summary>
        /// Handles NameSelection state
        /// </summary>
        /// <returns>The result of the handled input</returns>
        private CommandResult HandleNameSelection(string input, EntityAnimate entity)
        {
            if (InputValidation.ValidPlayerName(input))
            {
                // TODO: Build out additional authentication states and load player after authenticating
                entity.Name = input;
                var output = new OutputBuilder($"Welcome to HedronMUD, {entity.Name}!");
                entity.State = EntityState.Active;

                return CommandResult.Success(output.Output);
            }
            else
            {
                var output = new OutputBuilder("Please enter a valid player name containing only letters and between 3-12 characters.");

                output.Append("\nPlease enter your player name: ");
                return CommandResult.InvalidSyntax(output.Output);
            }
        }

        /// <summary>
        /// Handles Combat state
        /// </summary>
        /// <returns>The result of the handled input</returns>
        private CommandResult HandleCombatInput(string input, EntityAnimate entity)
        {
            return CommandResult.Failure("You must stop fighting first.");
        }
    }
}