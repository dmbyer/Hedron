using Hedron.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

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