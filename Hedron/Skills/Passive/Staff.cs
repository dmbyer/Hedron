using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Staff : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Staff()
        {
            FriendlyName = "staff";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}