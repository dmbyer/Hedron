using Hedron.Core.Entity.Living;
using Hedron.System;
using Hedron.System.Exceptions.Command;
using System;
using System.Collections.Generic;
using Hedron.Skills;
using Hedron.Skills.Passive;
using Hedron.Core.Entity.Base;
using System.Linq;

namespace Hedron.Commands.Skill
{
	public class Learn : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Learn()
		{
			FriendlyName = "learn";
			IsPlayerOnlyCommand = false;
			PrivilegeLevel = PrivilegeLevel.None;
			RequiresFullMatch = true;
			ValidStates.Add(Network.GameState.Active);
		}

		/// <summary>
		/// Learns a new skill
		/// </summary>
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

			EntityAnimate entity = commandEventArgs.Entity;

			if (commandEventArgs.Argument.ToLower() != "dodge")
				return CommandResult.InvalidSyntax(nameof(Learn), new List<string> { "dodge" });

			if (entity.Skills.Where(s => s.FriendlyName == "dodge").FirstOrDefault() == null)
			{
				entity.Skills.Add((ISkill)Activator.CreateInstance(typeof(Dodge)));
				return CommandResult.Success("You have learned the 'dodge' skill!");
			}
			else
			{
				return CommandResult.Failure("You already know the 'dodge' skill!");
			}
		}
	}
}