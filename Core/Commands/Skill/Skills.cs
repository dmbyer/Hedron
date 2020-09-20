using Hedron.Core.Entities.Base;
using Hedron.Core.Entities.Properties;
using Hedron.Core.Skills;
using Hedron.Core.System;
using Hedron.Core.System.Exceptions.Command;
using System.Collections.Generic;

namespace Hedron.Core.Commands.Skill
{
	public class Skills : Command
	{
		/// <summary>
		/// Default constructor
		/// </summary>
		public Skills()
		{
			FriendlyName = "skills";
			IsPlayerOnlyCommand = true;
			PrivilegeLevel = PrivilegeLevel.Player;
			RequiresFullMatch = false;
			ValidStates.Add(EntityState.Active);
		}

		/// <summary>
		/// Lists known skills
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

			List<ISkill> skills = new List<ISkill>();

			foreach (var s in entity.Skills)
				skills.Add(s);

			OutputBuilder result = new OutputBuilder();

			foreach (var s in skills)
				result.Append($"{SkillMap.SkillToFriendlyName(s.GetType())} [{(int)s.SkillLevel}]\n");

			return CommandResult.Success(result.Output);
		}
	}
}