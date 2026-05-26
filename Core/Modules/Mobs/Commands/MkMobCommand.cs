using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Commands
{
    public sealed class MkMobCommand : ICommand
    {
        private readonly IMobBuilderSystem _mobBuilder;
        private readonly IMobContentWriter _contentWriter;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "mkmob";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Create a mob in your current room.";
        public string LongDescription => "Creates an ad-hoc mob entity in your current room. Prints the blueprint id so you can use setmob to configure it.";
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
            EntityService entityService,
            IEventBus eventBus,
            IPersistenceSystem persistence)
        {
            _mobBuilder = mobBuilder;
            _contentWriter = contentWriter;
            _entityService = entityService;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "a mob";

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var result = _mobBuilder.CreateMob(name, location.RoomEntityId);

            await _contentWriter.WriteAsync(result.Template).ConfigureAwait(false);
            await _persistence.SaveEntityAsync(result.MobEntityId).ConfigureAwait(false);

            await _eventBus.PublishAsync(new MobCreatedByAdminEvent(
                context.InvokerEntityId,
                result.MobEntityId,
                result.BlueprintId,
                location.RoomEntityId)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Mob '{name}' created. Blueprint id: {result.BlueprintId}",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
