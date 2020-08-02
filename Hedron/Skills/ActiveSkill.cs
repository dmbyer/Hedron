using Hedron.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading.Tasks;

namespace Hedron.Skills
{
    public abstract class ActiveSkill : Command, ISkill
    {
        public int SkillLevel { get; set; } = 0;

        public float LearnRate { get; set; } = 1.0f;
    }
}
