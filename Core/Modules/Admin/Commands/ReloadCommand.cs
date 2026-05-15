using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Sessions;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>@reload</c>. Re-scans the content directory and refreshes the template
    /// registry. Newly authored templates with no live counterpart are seeded;
    /// <b>existing live entities are not modified</b>.
    /// </summary>
    /// <remarks>
    /// To pick up edits to a live room's description or components, restart the host. Use
    /// <c>@dig</c> for exit changes that should apply immediately.
    /// </remarks>
    public sealed class ReloadCommand : ICommand
    {
        private readonly IWorldContentLoader _loader;
        private readonly IAdminAuthorizer _authorizer;
        private readonly IEventBus _eventBus;

        public string Name => "@reload";
        public IReadOnlyList<string> Aliases { get; } = System.Array.Empty<string>();

        public ReloadCommand(
            IWorldContentLoader loader,
            IAdminAuthorizer authorizer,
            IEventBus eventBus)
        {
            _loader = loader;
            _authorizer = authorizer;
            _eventBus = eventBus;
        }

        public async Task ExecuteAsync(ISession session, string arguments)
        {
            if (!_authorizer.IsPrivileged(session))
            {
                await session.SendLineAsync("You are not authorized to use that command.")
                    .ConfigureAwait(false);
                return;
            }

            var result = await _loader.ReloadAsync().ConfigureAwait(false);

            await session.SendLineAsync(
                $"Reloaded content: {result.TemplatesLoaded} new, {result.TemplatesUnchanged} unchanged, " +
                $"{result.TemplatesRemoved} removed. Existing live entities were not modified.")
                .ConfigureAwait(false);

            await _eventBus.PublishAsync(new ContentReloadedEvent(
                result.TemplatesLoaded,
                result.TemplatesUnchanged,
                result.TemplatesRemoved)).ConfigureAwait(false);
        }
    }
}
