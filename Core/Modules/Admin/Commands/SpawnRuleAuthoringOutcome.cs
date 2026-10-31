using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Initiator-tier helper shared by every command that authors a room spawn rule
    /// (<c>spawnrule</c>, <c>mkmob</c>, <c>mkitem</c>): after a successful
    /// <see cref="IRoomBuilderSystem"/> mutation it writes the room YAML and publishes
    /// <see cref="SpawnRuleAuthoredByAdminEvent"/>. Called only from command-tier code, so it
    /// inherits their publish permission; the builder itself never writes or publishes (INV-5).
    /// Registered as a concrete singleton (precedent: <c>AbilityInvocationPipeline</c>).
    /// </summary>
    public sealed class SpawnRuleAuthoringOutcome
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IRoomContentWriter _roomWriter;
        private readonly IEventBus _eventBus;

        public SpawnRuleAuthoringOutcome(ITemplateRegistry templateRegistry, IRoomContentWriter roomWriter, IEventBus eventBus)
        {
            _templateRegistry = templateRegistry;
            _roomWriter = roomWriter;
            _eventBus = eventBus;
        }

        /// <summary>Writes the mutated room's YAML and publishes the audit event. Call only on a successful result.</summary>
        public async Task CompleteAsync(uint adminEntityId, uint roomEntityId, SpawnRuleMutationResult result, SpawnRuleChange change)
        {
            if (_templateRegistry.TryGet(result.RoomBlueprintId, out var tpl) && tpl is RoomTemplate roomTemplate)
                await _roomWriter.WriteAsync(roomTemplate).ConfigureAwait(false);

            var rule = result.Rule!;
            await _eventBus.PublishAsync(new SpawnRuleAuthoredByAdminEvent(
                adminEntityId, roomEntityId, result.RoomBlueprintId, rule.Id, change,
                rule.BlueprintId, rule.Count, rule.RespawnDelaySeconds)).ConfigureAwait(false);
        }

        /// <summary>
        /// Prints each out-of-designation finding the builder attached to the result as a warning
        /// line (R13). Advisory output only: the rule is already saved, and the finding is not an
        /// audit fact, so it is not added to the event. The judgement came from the builder; this
        /// only renders <see cref="PlacementFinding.Describe"/>.
        /// </summary>
        public async Task WriteFindingsAsync(CommandContext context, SpawnRuleMutationResult result)
        {
            if (result.Findings is null)
                return;

            foreach (var finding in result.Findings)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "Warning: " + finding.Describe(), OutputSeverity.System, OutputCategory.System))
                    .ConfigureAwait(false);
            }
        }
    }
}
