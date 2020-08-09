using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Bow : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Bow()
        {
            FriendlyName = "bow";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}