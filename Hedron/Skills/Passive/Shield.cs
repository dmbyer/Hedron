using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Shield : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Shield()
        {
            FriendlyName = "shield";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}