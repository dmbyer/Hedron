using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>mkarea [name]</c>.
    /// Creates an ad-hoc area entity and prints its blueprint id.
    /// </summary>
    public sealed class MkareaCommand : ICommand
    {
        private readonly IAreaBuilderSystem _areaBuilder;
        private readonly IAreaContentWriter _contentWriter;
        private readonly IEventBus _eventBus;

        public string Name => "mkarea";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Create an area.";
        public string LongDescription => "Creates an ad-hoc area entity and prints the blueprint id so you can configure it with setarea.";
        public string Usage => "mkarea [name]";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
        {
            new CommandArgument("name", typeof(string), CommandArgumentKind.RestOfLine,
                Required: false, "Name for the area (default: \"New Area\")."),
        });

        public MkareaCommand(
            IAreaBuilderSystem areaBuilder,
            IAreaContentWriter contentWriter,
            IEventBus eventBus)
        {
            _areaBuilder = areaBuilder;
            _contentWriter = contentWriter;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var name = context.Args.TryGet<string>("name", out var rawName) && rawName.Length > 0
                ? rawName : "New Area";

            var result = _areaBuilder.CreateArea(name);

            await _contentWriter.WriteAsync(result.Template).ConfigureAwait(false);

            await _eventBus.PublishAsync(new AreaCreatedByAdminEvent(
                context.InvokerEntityId,
                result.AreaEntityId,
                result.BlueprintId)).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Area '{name}' created. Blueprint id: {result.BlueprintId}",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);
        }
    }
}
