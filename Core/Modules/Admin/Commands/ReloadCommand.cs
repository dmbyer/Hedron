using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>reload</c>. Re-scans the content directory and refreshes the template
    /// registry. Newly authored templates with no live counterpart are seeded;
    /// <b>existing live entities are not modified</b>. Privilege enforced by dispatcher.
    /// </summary>
    public sealed class ReloadCommand : ICommand
    {
        private readonly IWorldContentLoader _loader;
        private readonly IEventBus _eventBus;

        public string Name => "reload";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Reload the content directory without restart.";
        public string LongDescription =>
            "Re-scans the content directory and refreshes the template registry. " +
            "Newly authored templates with no live counterpart are seeded. " +
            "Existing live entities are not modified — descriptions, exits, and components on rooms " +
            "that already exist will not change. " +
            "To pick up edits to a live room, restart, or use 'dig' for exit changes.";
        public string Usage => "reload";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public ReloadCommand(IWorldContentLoader loader, IEventBus eventBus)
        {
            _loader = loader;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            var result = await _loader.ReloadAsync().ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"Reloaded: {result.TemplatesLoaded} new, {result.TemplatesUnchanged} unchanged, " +
                $"{result.TemplatesRemoved} removed. Existing live entities were not modified.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);

            await _eventBus.PublishAsync(new ContentReloadedEvent(
                result.TemplatesLoaded,
                result.TemplatesUnchanged,
                result.TemplatesRemoved)).ConfigureAwait(false);
        }
    }
}
