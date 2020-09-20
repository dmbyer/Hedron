using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
{
    public class Staff : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Staff()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}