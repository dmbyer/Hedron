using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Sword : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Sword()
        {
            FriendlyName = "sword";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}