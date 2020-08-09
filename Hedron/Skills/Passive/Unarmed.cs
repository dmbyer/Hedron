using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Unarmed : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Unarmed()
        {
            FriendlyName = "unarmed";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}