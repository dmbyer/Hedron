using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Items.Commands
{
    /// <summary>
    /// Admin verb <c>mkitem [name]</c>.
    /// Creates an ad-hoc item entity in the invoker's current room and prints its blueprint id.
    /// </summary>
    public sealed class MkitemCommand : ICommand
    {
        private readonly IItemBuilderSystem _itemBuilder;
        private readonly IItemContentWriter _contentWriter;
        private readonly EntityService _entityService;
        private readonly IEventBus _eventBus;

        public string Name => "mkitem";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Create an item in your current room.";
        public string LongDescription => "Creates an ad-hoc item entity in your current room. Prints the blueprint id so you can use setitem to configure it.";
        public string Usage => "mkitem [name]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("name", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name for the item (default: \"an item\")."),
        });

        public MkitemCommand(
            IItemBuilderSystem itemBuilder,
            IItemContentWriter contentWriter,
            EntityService entityService,
            IEventBus eventBus)
        {
            _itemBuilder = itemBuilder;
            _contentWriter = contentWriter;
            _entityService = entityService;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "an item";

            if (!_entityService.TryGet<LocationComponent>(context.InvokerEntityId, out var location))
            {
                await context.Output.WriteAsync(
                    new PlainMessage("You have no location.", OutputSeverity.Error))
                    .ConfigureAwait(false);
                return;
            }

            var result = _itemBuilder.CreateItem(name, location.RoomEntityId);

            await _eventBus.PublishAsync(new ItemCreatedByAdminEvent(
                context.InvokerEntityId,
                result.ItemEntityId,
                result.BlueprintId,
                location.RoomEntityId)).ConfigureAwait(false);

            await _contentWriter.WriteAsync(result.Template).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Item '{name}' created. Blueprint id: {result.BlueprintId}",
                OutputSeverity.Confirmation)).ConfigureAwait(false);
        }
    }
}
