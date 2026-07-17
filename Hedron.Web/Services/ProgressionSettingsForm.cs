using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Stats;

namespace Hedron.Web.Services
{
    /// <summary>
    /// Mutable form-model backing a <see cref="ProgressionSettings"/> in the Simulation page's
    /// composer (sim-4) — the kind + settings ↔ form mapping the Simulation page round-trips
    /// through, kept out of the razor page so it is independently testable (parity with
    /// <see cref="CombatantForm"/>). Plain data — no validation here; <c>ISimScenarioStore.Validate</c>
    /// is the single validation seam (INV-19).
    /// </summary>
    public sealed class ProgressionSettingsForm
    {
        public ScoreId TargetTrack { get; set; } = ScoreId.Body;
        public int TargetImprovements { get; set; } = 1;
        public int MaxKillsPerRun { get; set; } = 100;
        public double? TicksPerKill { get; set; }

        public ProgressionSettings ToSettings() =>
            new(TargetTrack, TargetImprovements, MaxKillsPerRun, TicksPerKill);

        public void ApplyFrom(ProgressionSettings settings)
        {
            TargetTrack = settings.TargetTrack;
            TargetImprovements = settings.TargetImprovements;
            MaxKillsPerRun = settings.MaxKillsPerRun;
            TicksPerKill = settings.TicksPerKill;
        }
    }
}
