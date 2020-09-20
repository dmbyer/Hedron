using Hedron.Core.Entities.Properties;
using Hedron.Core.System.Exceptions.Command;
using System.Collections.Generic;
using Hedron.Core.Skills.Passive;
using Hedron.Core.Entities.Base;

namespace Hedron.Core.Commands.Skill
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
			ValidStates.Add(EntityState.Active);
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