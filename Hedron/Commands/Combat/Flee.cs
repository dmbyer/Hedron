using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Combat;
using Hedron.Core;
using Hedron.System;
using Hedron.System.Exceptions;

namespace Hedron.Commands.Combat
{
	/// <summary>
	/// Flees from combat
	/// </summary>
	public class Flee : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Flee()
		{
			FriendlyName = "flee";
			IsCombatCommand = true;
			PrivilegeLevel = PrivilegeLevel.NPC;
			ValidStates.Add(Network.GameState.Combat);
		}

		/// <summary>
		/// Runs from combat
		/// </summary>
		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (StateException)
			{
				return CommandResult.Failure("You have nothing to flee from.");
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			CombatHandler.Exit(commandEventArgs.Entity.Instance);
			return CommandResult.Success("You run from combat.");
		}
	}
}