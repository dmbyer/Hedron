using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;

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
        IReadOnlyList<Requirement>? LearnReqs = null
    );
}
