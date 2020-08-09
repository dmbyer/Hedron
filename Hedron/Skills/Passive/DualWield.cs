using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class DualWield : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public DualWield()
        {
            FriendlyName = "dual wield";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}