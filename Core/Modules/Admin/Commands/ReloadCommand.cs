using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Admin.Commands
{
    /// <summary>
    /// Admin verb <c>reload</c>. Rebuilds the live world instance from the YAML content directory,
    /// the same way a restart does — without dropping connected players. Privilege enforced by
    /// dispatcher.
    /// </summary>
    /// <remarks>
    /// Orchestration (Initiator, INV-8): (1) force-save all persistent state via
    /// <see cref="IPersistenceSystem.FlushAllAsync"/> so player/player-owned data is durable before
    /// the rebuild; (2) <see cref="IWorldContentLoader.ReloadAsync"/> tears down every world-content
    /// entity and re-spawns the world fresh from YAML (persistent entities survive); (3) publish
    /// <see cref="WorldContentReadyEvent"/> so the same post-load fan-out as startup runs — shops
    /// re-seed, spawn slots rebuild, and <c>CharacterHydrationHandler</c> re-resolves each player's
    /// room (resetting to the starting room if their room was removed from YAML). The audit
    /// <see cref="ContentReloadedEvent"/> is published last. Because the rebuild destroys and
    /// re-creates world entities, runtime instance state is reset: picked-up world items respawn,
    /// depleted shop stock refills, and the buy-back shelf clears — exactly like a restart.
    /// <para>
    /// <b>Refusal.</b> The loader stages and validates the whole corpus before teardown; on a content
    /// shape error it throws <see cref="ContentLoadException"/> with the live world untouched. The
    /// command then writes one error line per file and publishes neither
    /// <see cref="WorldContentReadyEvent"/> nor <see cref="ContentReloadedEvent"/>.
    /// </para>
    /// </remarks>
    public sealed class ReloadCommand : ICommand
    {
        private readonly IWorldContentLoader _loader;
        private readonly IEventBus _eventBus;
        private readonly IPersistenceSystem _persistence;

        public string Name => "reload";
        public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
        public CommandCategory Category => CommandCategory.Admin;
        public CommandMatchingMode MatchingMode => CommandMatchingMode.Full;
        public string ShortDescription => "Rebuild the world from content files without restart.";
        public string LongDescription =>
            "Rebuilds the live world from the content directory the same way a restart does, " +
            "without dropping connected players. Player state is force-saved first, then every " +
            "world entity (rooms, mobs, items, shop stock) is torn down and re-spawned fresh from " +
            "YAML — so edits to existing rooms/mobs/items take effect, picked-up items respawn, and " +
            "depleted shops refill. Players whose room was removed from YAML are moved to the " +
            "starting room. If any content file has a shape error (e.g. a legacy spawn key), the " +
            "reload is refused as a whole: each offending file is listed and the live world is " +
            "left untouched.";
        public string Usage => "reload";
        public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
            new IAuthorizationRequirement[] { new AdminRequirement() };
        public CommandArgumentSchema ArgumentSchema => CommandArgumentSchema.Empty;

        public ReloadCommand(IWorldContentLoader loader, IEventBus eventBus, IPersistenceSystem persistence)
        {
            _loader = loader;
            _eventBus = eventBus;
            _persistence = persistence;
        }

        public async Task ExecuteAsync(CommandContext context)
        {
            // 1. Force-save persistent state before tearing down the world instance.
            await _persistence.FlushAllAsync().ConfigureAwait(false);

            // 2. Rebuild the world from YAML (stages + validates first; on a shape error nothing
            //    is torn down and nothing is published).
            ContentReloadResult result;
            try
            {
                result = await _loader.ReloadAsync().ConfigureAwait(false);
            }
            catch (ContentLoadException ex)
            {
                var lines = new List<string>(ex.Errors.Count + 1)
                {
                    $"Reload refused — {ex.Errors.Count} content shape error(s); the live world is unchanged:",
                };
                foreach (var error in ex.Errors)
                    lines.Add($"  {error}");
                await context.Output.WriteAsync(new PlainMessage(
                    string.Join(Environment.NewLine, lines),
                    OutputSeverity.Error, OutputCategory.System)).ConfigureAwait(false);
                return;
            }

            // 3. Re-run the post-load fan-out (shop re-seed, spawn slots, player re-placement)
            //    exactly as the startup path does.
            await _eventBus.PublishAsync(new WorldContentReadyEvent()).ConfigureAwait(false);

            await context.Output.WriteAsync(new PlainMessage(
                $"World rebuilt from content: {result.TemplatesLoaded} new, {result.TemplatesUnchanged} unchanged, " +
                $"{result.TemplatesRemoved} removed. Runtime instance state was reset.",
                OutputSeverity.Confirmation, OutputCategory.System)).ConfigureAwait(false);

            // 4. Audit.
            await _eventBus.PublishAsync(new ContentReloadedEvent(
                result.TemplatesLoaded,
                result.TemplatesUnchanged,
                result.TemplatesRemoved)).ConfigureAwait(false);
        }
    }
}
