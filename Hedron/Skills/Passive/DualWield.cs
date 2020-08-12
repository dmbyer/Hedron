using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class DualWield : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public DualWield()
        {
            LearnRate = 0.25;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}