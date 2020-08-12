using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Sword : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Sword()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}