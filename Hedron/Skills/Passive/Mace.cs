using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Mace : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Mace()
        {
            FriendlyName = "mace";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}