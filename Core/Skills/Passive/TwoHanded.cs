using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
{
    public class TwoHanded : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public TwoHanded()
        {
            LearnRate = 0.25;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}