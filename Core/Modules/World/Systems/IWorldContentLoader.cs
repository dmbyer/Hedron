using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Domain system that scans the configured content directory, registers authored
    /// templates with <c>ITemplateRegistry</c>, and seeds room/area entities into the
    /// live world (skipping entities that were hydrated from disk by persistence).
    /// </summary>
    public interface IWorldContentLoader
    {
        /// <summary>
        /// Initial startup load. Discovers and registers all templates, then spawns the
        /// rooms and areas that aren't already in the live world. Resolves
        /// <c>WorldConfiguration.StartingRoomEntityId</c> from the configured starting
        /// blueprint id. Called once by <c>WorldContentBootstrap</c>.
        /// </summary>
        Task LoadAndSpawnAsync(CancellationToken ct = default);

        /// <summary>
        /// Re-scan the content directory and refresh the registry. Templates that have
        /// no live counterpart are seeded; existing live entities are not mutated.
        /// Returns the result counts that <c>ReloadCommand</c> wraps in
        /// <c>ContentReloadedEvent</c>.
        /// </summary>
        Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default);
    }

    /// <summary>Result of <see cref="IWorldContentLoader.ReloadAsync"/>.</summary>
    public readonly record struct ContentReloadResult(
        int TemplatesLoaded,
        int TemplatesUnchanged,
        int TemplatesRemoved);
}
