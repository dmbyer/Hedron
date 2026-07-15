using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Simulation
{
    /// <summary>
    /// The scenario-kind seam (seed OQ, sim-2 Design notes): <see cref="Combat"/> ships in this
    /// slice; <see cref="ProgressionRate"/> is reserved for sim-4's advancement-rate sweeps on the
    /// same runner. Adding a kind means adding an executor and a report payload section — the
    /// scenario store, runner shell, and report envelope never change (kind-generic by construction).
    /// </summary>
    public enum ScenarioKind
    {
        Combat,
        ProgressionRate,
    }

    /// <summary>
    /// Which family a <see cref="CombatantSpec"/> resolves through. Day-one sources per the seed:
    /// an authored mob template, a sim-1 standards-registry reference build, or an inline stat
    /// block (the future procedural-generator validation entry).
    /// </summary>
    public enum CombatantSourceKind
    {
        MobTemplate,
        ReferenceBuild,
        Inline,
    }

    /// <summary>
    /// A caller-authored stat block: raw <see cref="ScoreId"/> values plus an optional ability
    /// kit, resolved as-is with no registry/catalog lookup. The entry the future procedural mob
    /// generator uses to validate a candidate before writing YAML.
    /// </summary>
    public sealed record InlineStatBlock(
        IReadOnlyDictionary<ScoreId, int> Scores,
        IReadOnlyList<string> AbilityKit);

    /// <summary>
    /// One combatant in a <see cref="ScenarioSide"/>. <see cref="Tier"/>/<see cref="Band"/> are the
    /// (Tier, Band) cell — <b>required</b> when <see cref="Source"/> is
    /// <see cref="CombatantSourceKind.ReferenceBuild"/> (they select the cell), <b>optional</b>
    /// verdict-evaluation annotations when <see cref="Source"/> is
    /// <see cref="CombatantSourceKind.Inline"/>, and ignored for
    /// <see cref="CombatantSourceKind.MobTemplate"/> (the resolved template's own
    /// <c>Tier</c>/<c>Band</c> tag is the cell, when authored &gt;= band 1).
    /// </summary>
    public sealed record CombatantSpec(
        CombatantSourceKind Source,
        string PolicyId,
        string? MobBlueprintId = null,
        int? Tier = null,
        int? Band = null,
        InlineStatBlock? Inline = null);

    /// <summary>One side of a scenario. A list from day one — 1v1 is enforced in sim-2 (exactly one combatant), N-vs-N is additive data at a later slice (seed family table).</summary>
    public sealed record ScenarioSide(IReadOnlyList<CombatantSpec> Combatants);

    /// <summary>
    /// The whole run recipe: kind, iteration/termination knobs, and the two sides. YAML-authorable,
    /// editor-composable (sim-3), generator-constructable (later). Validated by
    /// <see cref="Systems.ISimScenarioStore"/> before any run executes.
    /// </summary>
    public sealed record ScenarioDefinition(
        ScenarioKind Kind,
        string Name,
        int Seed,
        int Iterations,
        int MaxTicksPerRun,
        IReadOnlyList<ScenarioSide> Sides);
}
