using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Dodge : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Dodge()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}