using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Skills
{
    public class PassiveSkill : ISkill
    {
        public string FriendlyName { get; set; }

        public int SkillLevel { get; set; } = 0;

        public float LearnRate { get; set; } = 1.0f;

        public int Cooldown { get; protected set; }
    }
}