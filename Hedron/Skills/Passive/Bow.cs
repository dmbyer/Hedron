using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Bow : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Bow()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}