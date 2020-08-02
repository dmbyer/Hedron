using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Skills
{
    public interface ISkill
	{
		/// <summary>
		/// The friendly name of the skill
		/// </summary>
		/// <remarks>Active skills inheriting from Command will already implement this.</remarks>
		public string FriendlyName { get; set; }

		/// <summary>
		/// The level of the skill
		/// </summary>
		public int SkillLevel { get; set; }

		/// <summary>
		/// The rate at which this skill progresses based on use.
		/// </summary>
		public float LearnRate { get; set; }
	}
}
