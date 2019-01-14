using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Network;

namespace Hedron.Commands
{
	public abstract class Command
	{
		/// <summary>
		/// The friendly name of the command
		/// </summary>
		public string FriendlyName { get; set; } = "";

		/// <summary>
		/// Whether this command has priority execution
		/// </summary>
		public bool HasPriority { get; set; } = false;

		/// <summary>
		/// Whether this is a combat-specific command that should queue
		/// </summary>
		public bool IsCombatCommand { get; set; } = false;
		
		/// <summary>
		/// Sets whether the command can only be executed by players for the purposes of type casting
		/// </summary>
		public bool IsPlayerOnlyCommand { get; set; } = false;

		/// <summary>
		/// The minimum privilege level required to execute
		/// </summary>
		public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.None;

		/// <summary>
		/// Whether this command requires a full text match to be executed
		/// </summary>
		public bool RequiresFullMatch { get; set; } = false;

		/// <summary>
		/// The valid states this command may be executed in
		/// </summary>
		public List<GameState> ValidStates { get; set; } = new List<GameState>();

		/// <summary>
		/// Default constructor
		/// </summary>
		public Command()
		{

		}

		/// <summary>
		/// Execute the command
		/// </summary>
		/// <param name="args">The argments for the command</param>
		/// <returns>The command result</returns>
		public virtual CommandResult Execute(CommandEventArgs args)
		{
			// TODO: Check state, privilege level, overriden privilege level
			// TODO: Create exceptions for the above failures
			// TODO: Throw exception on failure, allow inherited classes to handle the exception

			// Check for null args


			// Check for null entity


			// Check privilege level


			// Check state
			



			return CommandResult.Success("");
		}
	}
}