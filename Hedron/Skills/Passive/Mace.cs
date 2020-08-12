using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Mace : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Mace()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}