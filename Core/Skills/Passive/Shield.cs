using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
{
    public class Shield : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Shield()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}