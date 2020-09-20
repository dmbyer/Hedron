using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Skills
{
	public class ImproveSkillResult
	{
		/// <summary>
		/// The amount the skill improved by
		/// </summary>
		public double ImprovedBy { get; private set; }

		/// <summary>
		/// The improvement message
		/// </summary>
		public string ImprovedMessage { get; private set; }

		/// <summary>
		/// Whether the skill was added prior to improving due to it not having been learned
		/// </summary>
		public bool WasAdded { get; private set; }

		/// <summary>
		/// The friendly display name of the skill
		/// </summary>
		/// <remarks>This is a helper property useful in cases where the skill being improved was known only at runtime for easier message building</remarks>
		public string FriendlyName { get; private set; }

		/// <summary>
		/// Creates a new Improved Skill Result
		/// </summary>
		/// <param name="improvedBy">The amount the skill improved by</param>
		/// <param name="improvedMessage">The improvement message</param>
		/// <param name="wasAdded">Whether the skill was added due to it having been previously unlearned</param>
		public ImproveSkillResult(double improvedBy, string improvedMessage, bool wasAdded, string friendlyName)
		{
			ImprovedBy = improvedBy;
			ImprovedMessage = improvedMessage;
			WasAdded = wasAdded;
			FriendlyName = friendlyName;
		}
	}
}
