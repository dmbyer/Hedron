using Hedron.Commands;
using Hedron.Core.Entity.Base;
using Hedron.Core.Locale;
using Hedron.Data;
using Hedron.System;
using System.Collections.Generic;

namespace Hedron.Network
{
    /// <summary>
    /// Handles input from player based on game state
    /// </summary>
    public class StateHandler
    {
        /// <summary>
        /// The current input state for the player
        /// </summary>
        public GameState State { get; set; }

        /// <summary>
        /// Constructor; sets default state to NameSelection
        /// </summary>
        public StateHandler()
        {
            State = GameState.NameSelection;
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

            switch (State)
            {
                case GameState.NameSelection:
                    CommandResult result = HandleNameSelection(input, entity);

                    if (result.ResultCode == ResultCode.SUCCESS)
                    {
                        entity.IOHandler?.QueueOutput(result.ResultMessage);
                        var startingRooms = DataAccess.GetInstancesOfPrototype<Room>(DataAccess.Get<World>(0, CacheType.Instance).StartingLocation);

                        var args = new CommandEventArgs(
                            startingRooms.Count > 0 ? startingRooms[0].Instance.ToString() : "0",
                            entity,
                            null);

                        State = GameState.Active;
                        result = new Commands.Movement.Goto().Execute(args);
                    }

                    return result;

                case GameState.Active:
                    return CommandHandler.ProcessCommand(input, entity, new List<GameState> { GameState.Active });

                case GameState.Combat:
                    var processCommands = CommandHandler.ProcessCommand(input, entity, new List<GameState> { GameState.Combat });

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
                entity.Name = input;
                var output = new OutputBuilder($"Welcome to HedronMUD, {entity.Name}!");

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