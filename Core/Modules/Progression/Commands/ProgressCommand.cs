using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Progression.Commands
{
    /// <summary>
    /// Player verb <c>progress</c> — the functional-validation "see it work" gate for the
    /// progression substrate. Lists each track's improvement count, cumulative XP, and
    /// XP-to-next-threshold for the invoking entity.
    /// </summary>
    public sealed class ProgressCommand : ICommand
    {
        private readonly IProgressionSystem _progressionSystem;

        public string Name => "progress";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Player;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial;
        public bool UsableWhileIncapacitated => true;
        public string ShortDescription => "Display your progression tracks.";
        public string LongDescription =>
            "Shows each attribute/pool track's improvement count, cumulative experience, and experience needed " +
            "to reach the next improvement, followed by a separate block for the abilities you have earned rank in. " +
            "Ability rank is display-only — it does not yet grant power.";
        public string Usage => "progress";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            Array.Empty<IAuthorizationRequirement>();
        public CommandArgumentSchema ArgumentSchema { get; } = new(Array.Empty<CommandArgument>());

        public ProgressCommand(IProgressionSystem progressionSystem)
        {
            _progressionSystem = progressionSystem;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var entityId = context.InvokerEntityId;

            var tracked = _progressionSystem.GetTrackedTracks(entityId);

            ProgressTrackRow RowFor(ProgressionTrack track) => new(
                track,
                _progressionSystem.GetImprovementCount(entityId, track),
                _progressionSystem.GetXp(entityId, track),
                _progressionSystem.GetXpToNextThreshold(entityId, track));

            var scoreRows = tracked
                .Where(track => track.IsScore)
                .OrderBy(track => track.Score)
                .Select(RowFor)
                .ToList();

            var abilityRows = tracked
                .Where(track => track.IsAbility)
                .OrderBy(track => track.AbilityId, StringComparer.Ordinal)
                .Select(RowFor)
                .ToList();

            await context.Output.WriteAsync(new ProgressDisplayMessage(scoreRows, abilityRows)).ConfigureAwait(false);
        }
    }
}
