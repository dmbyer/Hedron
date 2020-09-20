using Hedron.Core.Combat;
using Hedron.Core.Entities.Properties;
using Hedron.Core.System.Exceptions.Command;

namespace Hedron.Core.Commands.Combat
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
			ValidStates.Add(EntityState.Combat);
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