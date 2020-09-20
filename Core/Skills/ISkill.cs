using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Skills
{
	public interface ISkill
	{
		/// <summary>
		/// The level of the skill
		/// </summary>
		public double SkillLevel { get; set; }

		/// <summary>
		/// The rate at which this skill progresses based on use.
		/// </summary>
		public double LearnRate { get; set; }

		/// <summary>
		/// The cooldown of the skill, measured in ticks
		/// </summary>
		public int Cooldown { get; }
	}
}