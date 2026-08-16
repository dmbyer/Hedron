using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Abilities
{
    public enum AbilityKind { Skill, Spell }
    public enum Activation { Active, Passive, Triggered }
    public enum Targeting { Self, Target }

    // Placeholder stub types — carried on definition, not yet wired/resolved
    public sealed record TriggerCondition(string Condition);
    public sealed record ImprovementCurve(string Kind);
    public sealed record Requirement(string Kind, string Value);

    public sealed record ResourceCost(ResourceType Resource, int Amount);

    public sealed record AbilityDefinition(
        string Id,
        string Name,
        AbilityKind Kind,
        Activation Activation,
        Targeting Targeting,
        IReadOnlyList<ResourceCost> Costs,
        IReadOnlyList<string> Effects,
        float CooldownSeconds,
        AspectComposition? Aspect = null,
        TriggerCondition? Trigger = null,
        ImprovementCurve? Curve = null,
        IReadOnlyList<Requirement>? LearnReqs = null,

        /// <summary>
        /// Per-ability granular XP scale (R7) applied to every <c>XpSource.AbilityUse</c> award
        /// this ability produces. <c>1.0</c> is the shipped default; <c>0</c> makes the ability
        /// grant no experience at all. Authored as a compiled row in <see cref="AbilityRegistry"/>
        /// (configuration Category 3) and inspected via <c>defs ability &lt;id&gt;</c> — abilities
        /// have no YAML pipeline yet, and building one is a slice of its own (see backlog).
        /// </summary>
        double XpScale = 1.0,

        /// <summary>
        /// The attribute/pool track this ability's use routes its <b>score</b> XP to, alongside
        /// its own display-only rank track. <see langword="null"/> (the default) means using the
        /// ability grants rank only and no attribute power — the conservative default for any
        /// ability that has not been deliberately placed in the power model.
        /// </summary>
        ScoreId? XpAttributeTrack = null
    );
}
