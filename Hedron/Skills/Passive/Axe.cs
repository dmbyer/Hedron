﻿using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Axe : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Axe()
        {
            FriendlyName = "axe";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}