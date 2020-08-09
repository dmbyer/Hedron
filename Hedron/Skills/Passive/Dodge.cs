﻿using Hedron.System;

namespace Hedron.Skills.Passive
{
    public class Dodge : PassiveSkill
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public Dodge()
        {
            FriendlyName = "dodge";
            LearnRate = 1.0f;
            Cooldown = Constants.COOLDOWN_TIME_NONE;
        }
    }
}