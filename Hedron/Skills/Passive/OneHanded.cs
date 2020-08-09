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
            FriendlyName = "one handed";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}