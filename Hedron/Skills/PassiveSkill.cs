using Hedron.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Skills
{
    public abstract class PassiveSkill : ISkill
    {
        public double SkillLevel { get; set; } = 1;

        public double LearnRate { get; set; } = 1.0f;

        public int Cooldown { get; protected set; }
    }
}