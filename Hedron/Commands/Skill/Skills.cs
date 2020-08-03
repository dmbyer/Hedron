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
			ValidStates.Add(Network.GameState.Active);
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
				result.Append($"{s.FriendlyName}\n");

			return CommandResult.Success(result.Output);
		}
	}
}