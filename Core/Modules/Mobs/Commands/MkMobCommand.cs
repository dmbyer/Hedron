using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Mobs.Commands
{
    /// <summary>
    /// Admin verb <c>mkmob [name]</c>. Registers a new mob template and adds a count-1 respawning
    /// spawn rule for it to the invoker's room, so the confirmed mob is that rule's tracked instance.
    /// </summary>
    /// <remarks>
    /// Orchestration (Initiator, INV-8, INV-21): (1) validate the room <b>first</b> (a refusal leaves
    /// no orphan template or file); (2) <see cref="IMobBuilderSystem.CreateMob"/> registers the
    /// template, written to YAML; (3) <see cref="IRoomBuilderSystem.AddSpawnRule"/> updates the room
    /// template, live component and tracker (spawning the instance); (4) publish the creation event, then
    /// <see cref="SpawnRuleAuthoringOutcome"/> writes the room YAML and publishes <see cref="MobCreatedByAdminEvent"/> and <see cref="SpawnRuleAuthoredByAdminEvent"/>.
    /// </remarks>
    public sealed class MkMobCommand : ICommand
    {
        private readonly IMobBuilderSystem _mobBuilder;
        private readonly IMobContentWriter _contentWriter;
        private readonly IRoomBuilderSystem _roomBuilder;
        private readonly SpawnRuleAuthoringOutcome _ruleOutcome;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "mkmob";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Create a mob in your current room.";
        public string LongDescription =>
            "Creates an ad-hoc mob blueprint and adds a spawn rule for it (count 1, respawn " +
            $"{SpawnRuleLimits.DefaultRespawnDelaySeconds}s) to your current room, so the mob appears now " +
            "and respawns after it dies. Prints the blueprint id so you can use setmob to configure it; " +
            "use spawnrule to change or remove the rule.";
        public string Usage => "mkmob [name]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("name", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name for the mob (default: \"a mob\")."),
        });

        public MkMobCommand(
            IMobBuilderSystem mobBuilder,
            IMobContentWriter contentWriter,
            IRoomBuilderSystem roomBuilder,
            SpawnRuleAuthoringOutcome ruleOutcome,
            EntityService entityService,
            IEventBus eventBus)
        {
            _mobBuilder = mobBuilder;
            _contentWriter = contentWriter;
            _roomBuilder = roomBuilder;
            _ruleOutcome = ruleOutcome;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "a mob";

            // 1. Validate the room before creating anything.
            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location) ||
                !_roomBuilder.TryGetAuthorableRoom(location.RoomEntityId, out _))
            {
                await context.Output.WriteAsync(new PlainMessage(
                    "You must be standing in a world room with a registered room template.",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }
            var roomId = location.RoomEntityId;

            // 2. Register the template and write it.
            var result = _mobBuilder.CreateMob(name);
            await _contentWriter.WriteAsync(result.Template).ConfigureAwait(false);

            // 3. Author the count-1 rule (spawns the instance).
            var rule = _roomBuilder.AddSpawnRule(
                roomId, result.BlueprintId, 1, SpawnRuleLimits.DefaultRespawnDelaySeconds);
            if (!rule.Success || rule.Rule is null)
            {
                await context.Output.WriteAsync(new PlainMessage(
                    $"Mob '{result.BlueprintId}' was created but its spawn rule was refused: {rule.Error}",
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }
            // 4. Publish the creation; write the room YAML and publish the rule event.
            var mobEntityId = rule.Sync.SpawnedEntityIds.FirstOrDefault();
            await _eventBus.PublishAsync(new MobCreatedByAdminEvent(
                context.InvokerEntityId,
                mobEntityId,
                result.BlueprintId,
                roomId)).ConfigureAwait(false);
            await _ruleOutcome.CompleteAsync(context.InvokerEntityId, roomId, rule, SpawnRuleChange.Added).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Mob '{name}' created. Blueprint id: {result.BlueprintId} (spawn rule '{rule.Rule.Id}' added here).",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
