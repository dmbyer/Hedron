using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Dagger : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Dagger()
        {
            FriendlyName = "dagger";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}