using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Wand : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Wand()
        {
            LearnRate = 1.0;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}