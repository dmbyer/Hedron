using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
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