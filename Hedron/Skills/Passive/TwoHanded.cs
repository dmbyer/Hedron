using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class TwoHanded : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public TwoHanded()
        {
            FriendlyName = "two handed";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}