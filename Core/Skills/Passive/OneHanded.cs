using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
{
    public class OneHanded : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public OneHanded()
        {
            LearnRate = 0.25;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}