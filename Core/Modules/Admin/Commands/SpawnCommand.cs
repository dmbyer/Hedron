using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>spawn &lt;blueprintId&gt;</c>. Resolves the blueprint via
    /// <see cref="ITemplateRegistry"/>, spawns a fresh entity, and publishes
    /// <see cref="EntitySpawnedByAdminEvent"/>. Privilege is enforced by the dispatcher.
    /// </summary>
    public sealed class SpawnCommand : ICommand
    {
        private readonly ITemplateRegistry _templateRegistry;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "spawn";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Spawn a template entity into the world.";
        public string LongDescription => "Spawns a templated entity into the current room. " +
            "Use 'dig' to wire newly spawned rooms into the live world.";
        public string Usage => "spawn <blueprintId>";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("blueprintId", typeof(string), CommandArgumentKind.Token,
                Required: true, "The blueprint id to spawn (e.g. room.crossroads)."),
        });

        public SpawnCommand(ITemplateRegistry templateRegistry, EntityService entityService, IEventBus eventBus)
        {
            _templateRegistry = templateRegistry;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var blueprintId = context.Args.Get<string>("blueprintId");

            if (!_templateRegistry.TryGet(blueprintId, out _))
            {
                await context.Output.WriteAsync(
                    new PlainMessage($"Unknown blueprint: {blueprintId}", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location — cannot spawn here.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var spawned = _templateRegistry.Spawn(blueprintId);

            // Room and area entities are orphan entities after spawn — use 'dig' to wire a new room in.
            // Mob entities are placed in the invoker's current room immediately.
            if (_entityService.HasComponent<MobDataComponent>(spawned.Id))
                _entityService.AddComponent(spawned.Id, new LocationComponent { RoomEntityId = location.RoomEntityId });

            await context.Output.WriteAsync(
                new PlainMessage($"Spawned {blueprintId} (entity #{spawned.Id}).", OutputSeverity.Confirmation))
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new EntitySpawnedByAdminEvent(
                context.InvokerEntityId,
                spawned.Id,
                blueprintId,
                location.RoomEntityId)).ConfigureAwait(false);
        }
    }
}
