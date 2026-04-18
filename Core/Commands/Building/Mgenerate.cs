using Core.ECS.Properties;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using System.Collections.Generic;

namespace Hedron.Core.Commands.Building
{
	/// <summary>
	/// Lists entities (temporarily disabled).
	/// </summary>
	/// <remarks>
	/// This command previously relied on <c>Hedron.Core.System.Autogeneration.AutogenMob</c>
	/// which was removed during the ECS restructuring. The admin surface is kept so that
	/// Wave 1 can reinstate it against the ECS-native mob generator without needing to
	/// re-register the command. See <c>docs/roadmap/api-alignment-plan.md</c>.
	/// </remarks>
	public class Mgenerate : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Mgenerate()
		{
			FriendlyName = "mgenerate";
			PrivilegeLevel = PrivilegeLevel.Builder;
			RequiresFullMatch = true;
			ValidStates.Add(EntityState.Active);
		}

		public override CommandResult Execute(CommandEventArgs commandEventArgs)
		{
			try
			{
				base.Execute(commandEventArgs);
			}
			catch (CommandException ex)
			{
				return ex.CommandResult;
			}

			return CommandResult.Failure(
				"mgenerate is temporarily disabled pending the ECS autogeneration port.");
		}
	}
}
