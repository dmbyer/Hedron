using Core.Modules.Skills;
using Hedron.Core.System;

namespace Core.Modules.Skills.Passive
{
    public class Axe : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Axe()
        {
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}