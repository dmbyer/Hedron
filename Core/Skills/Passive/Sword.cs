using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
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