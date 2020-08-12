using Hedron.System;

namespace Hedron.Skills.Passive
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