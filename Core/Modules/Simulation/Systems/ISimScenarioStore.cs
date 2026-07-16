namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// YAML load + fail-fast structural validation of <see cref="ScenarioDefinition"/>. Posture
    /// mirrors <c>BalanceStandardsStore</c>: validate-then-use, named violations thrown as a single
    /// <see cref="System.InvalidOperationException"/> listing every offense found. <see cref="Validate"/>
    /// is also callable directly on an in-memory definition — the seam the sim-3 editor and a future
    /// generator candidate-check reuse without a round-trip through disk.
    /// </summary>
    public interface ISimScenarioStore
    {
        /// <summary>
        /// Loads and validates the scenario file at <paramref name="path"/>.
        /// <paramref name="seedOverride"/>, when supplied, replaces the file's <c>seed</c> field
        /// (mirrors <c>generate --seed</c>). Throws <see cref="System.IO.FileNotFoundException"/>
        /// if the file does not exist, or <see cref="System.InvalidOperationException"/> on any
        /// structural violation.
        /// </summary>
        ScenarioDefinition Load(string path, int? seedOverride = null);

        /// <summary>
        /// Structurally validates an in-memory <see cref="ScenarioDefinition"/> — unknown kind,
        /// unknown policy id, empty side, wrong combatant count for the kind, non-positive
        /// iterations/maxTicksPerRun, an unresolvable combatant source, or a missing required
        /// source field. Throws <see cref="System.InvalidOperationException"/> naming every
        /// violation found; a valid scenario returns normally.
        /// </summary>
        void Validate(ScenarioDefinition scenario);
    }
}
