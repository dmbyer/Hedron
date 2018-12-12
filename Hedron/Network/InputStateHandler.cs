using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Hedron.Core;
using Hedron.System;
using Hedron.Data;
using Hedron.Commands;

namespace Hedron.Network
{
	/// <summary>
	/// Handles input from player based on game state
	/// </summary>
	public class StateHandler
	{
		/// <summary>
		/// Valid input states
		/// </summary>
		public enum State
		{
			NameSelection,
			LoggedIn
		}

		/// <summary>
		/// The current input state for the player
		/// </summary>
		public State InputState { get; set; }

		/// <summary>
		/// Constructor; sets default state to NameSelection
		/// </summary>
		public StateHandler()
		{
			InputState = State.NameSelection;
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
				return CommandResult.CMD_R_FAIL;

			switch (InputState)
			{
				case State.NameSelection:
					CommandResult result = HandleNameSelection(input, entity);

					if (result == CommandResult.CMD_R_SUCCESS)
						InputState = State.LoggedIn;

					return result;
				case State.LoggedIn:
					return CommandHandler.ProcessCommand(input, entity);
				default:
					return CommandResult.CMD_R_FAIL;
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
				entity.IOHandler.QueueOutput(string.Format("Welcome to HedronMUD, {0}!", entity.Name));

				var startingRooms = DataAccess.GetInstancesOfPrototype<Room>(DataAccess.Get<World>(0, CacheType.Instance).StartingLocation);
				CommandHandler.InvokeCommand(Command.CMD_GOTO, startingRooms.Count > 0 ? startingRooms[0].Instance.ToString() : "0", entity);
				return CommandResult.CMD_R_SUCCESS;
			}
			else
			{
				entity.IOHandler.QueueOutput("Please enter a valid player name containing only letters and between 3-12 characters.");
				entity.IOHandler.QueueOutput("\nPlease enter your player name: ");
				return CommandResult.CMD_R_ERRSYNTAX;
			}
		}
	}
}