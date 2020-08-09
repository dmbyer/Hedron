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
            FriendlyName = "wand";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}