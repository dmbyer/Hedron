using Hedron.Core.System;

namespace Hedron.Core.Skills.Passive
{
    public class Unarmed : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Unarmed()
        {
            LearnRate = 0.5;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}