using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>One completed progression-rate run's outcome — the reduce step's input.</summary>
    public sealed record ProgressionRunRecord(
        int RunIndex,
        bool ReachedTarget,
        int Kills,
        /// <summary>Kill count at improvement 1..N of the target track (capped at the run's reach point).</summary>
        IReadOnlyList<int> MilestoneKills,
        /// <summary>Final cumulative XP per <see cref="ProgressionConstants.CombatTracks"/>.</summary>
        IReadOnlyDictionary<ScoreId, int> FinalXp,
        /// <summary>Final improvement count per <see cref="ProgressionConstants.CombatTracks"/>.</summary>
        IReadOnlyDictionary<ScoreId, int> FinalImprovements);

    /// <summary>
    /// Drives one progression-rate run to completion: repeated analytical kill-events over the real
    /// <see cref="Progression.Systems.IProgressionSystem.AwardCombatExperience"/> seam — no combat
    /// rounds, no bus (INV-5). The victim is never destroyed; one award models one kill of a fresh
    /// identical spawn, exactly what live template respawn produces (see the plan's Design notes on
    /// the central executor decision). Stateless; safe to instantiate once per run or reuse across
    /// runs on the same thread.
    /// </summary>
    public sealed class ProgressionScenarioExecutor
    {
        public ProgressionRunRecord ExecuteRun(
            SandboxWorld world, uint subjectId, uint victimId, ProgressionSettings settings, int runIndex)
        {
            var milestoneKills = new List<int>();
            var kills = 0;
            var reachedTarget = false;

            while (kills < settings.MaxKillsPerRun)
            {
                kills++;

                var award = world.Progression.AwardCombatExperience(subjectId, victimId);

                // AwardOutcome.Track widened to ProgressionTrack when ability tracks landed; the
                // scenario contract is unchanged, so adapt at the ScoreId boundary here rather
                // than leaking the wider key into ProgressionSettings / ProgressionRunRecord.
                var targetTrack = ProgressionTrack.Of(settings.TargetTrack);
                var targetRow = award.Tracks.First(row => row.Track == targetTrack);

                for (var i = 0; i < targetRow.ImprovementsGained && milestoneKills.Count < settings.TargetImprovements; i++)
                    milestoneKills.Add(kills);

                if (milestoneKills.Count >= settings.TargetImprovements)
                {
                    reachedTarget = true;
                    break;
                }
            }

            var finalXp = new Dictionary<ScoreId, int>();
            var finalImprovements = new Dictionary<ScoreId, int>();
            foreach (var track in ProgressionConstants.CombatTracks)
            {
                finalXp[track] = world.Progression.GetXp(subjectId, track);
                finalImprovements[track] = world.Progression.GetImprovementCount(subjectId, track);
            }

            return new ProgressionRunRecord(runIndex, reachedTarget, kills, milestoneKills, finalXp, finalImprovements);
        }
    }
}
