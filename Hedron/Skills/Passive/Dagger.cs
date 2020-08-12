using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Dagger : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Dagger()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}