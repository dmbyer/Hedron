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
			string skillName = SkillMap.SkillToFriendlyName(typeof(Dodge));

			if (commandEventArgs.Argument.ToLower() != skillName)
				return CommandResult.InvalidSyntax(nameof(Learn), new List<string> { skillName });

			return CommandResult.Success(entity.ImproveSkill(skillName, 0).ImprovedMessage);
		}
	}
}