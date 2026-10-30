using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;

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
        /// rooms and areas that aren't already in the live world, then runs
        /// <see cref="ResolveStartingRoom"/>. Called once by <c>WorldContentBootstrap</c>.
        /// </summary>
        Task LoadAndSpawnAsync(CancellationToken ct = default);

        /// <summary>
        /// Re-scan the content directory and refresh the registry. Templates that have
        /// no live counterpart are seeded; existing live entities are not mutated.
        /// Returns the result counts that <c>ReloadCommand</c> wraps in
        /// <c>ContentReloadedEvent</c>.
        /// </summary>
        Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default);

        /// <summary>
        /// Re-reads <c>world.yaml</c>, resolves the starting room over the live <b>room</b> set
        /// (manifest → <c>World:StartingRoomBlueprintId</c> → <c>room.void</c> → ordinal-least room),
        /// publishes the result to <c>WorldConfiguration.StartingRoom</c> as one snapshot, and
        /// returns it. Never throws for a missing room or malformed manifest — it logs and falls back.
        /// </summary>
        ResolvedStartingRoom ResolveStartingRoom();
    }

    /// <summary>Result of <see cref="IWorldContentLoader.ReloadAsync"/>.</summary>
    public readonly record struct ContentReloadResult(
        int TemplatesLoaded,
        int TemplatesUnchanged,
        int TemplatesRemoved);
}
