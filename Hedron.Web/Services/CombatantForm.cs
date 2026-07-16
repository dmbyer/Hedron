using Hedron.Core.Modules.Simulation;

namespace Hedron.Web.Services
{
    /// <summary>
    /// Mutable form-model backing one <see cref="CombatantSpec"/> in the Simulation page's
    /// composer (and, from sim-3 WP3, an entry-point prefill target). Plain data — no validation
    /// here; <c>ISimScenarioStore.Validate</c> is the single validation seam (INV-19).
    /// </summary>
    public sealed class CombatantForm
    {
        public CombatantSourceKind Source { get; set; } = CombatantSourceKind.ReferenceBuild;
        public string PolicyId { get; set; } = string.Empty;
        public string MobBlueprintId { get; set; } = string.Empty;
        public int? Tier { get; set; }
        public int? Band { get; set; }
        public string InlineScoresCsv { get; set; } = string.Empty;
        public string InlineAbilityKitCsv { get; set; } = string.Empty;
    }
}
