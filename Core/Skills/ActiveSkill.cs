using Hedron.Core.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Core.Skills
{
    public abstract class ActiveSkill : Command, ISkill
    {
        public double SkillLevel { get; set; } = 1;

        public double LearnRate { get; set; } = 1.0f;

        public int Cooldown { get; protected set; }
    }
}