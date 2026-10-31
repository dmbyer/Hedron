using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Systems.Definitions;

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
        /// Initial startup load. Reads every content file first (staged); a content shape error in
        /// any file throws <see cref="Hedron.Core.Systems.ContentLoadException"/> listing them all, with
        /// nothing registered. Otherwise registers all templates, spawns the rooms and areas that
        /// aren't already in the live world (never mobs/items — room spawn rules place those), then
        /// runs <see cref="ResolveStartingRoom"/>, then every <see cref="IContentReloadParticipant"/>
        /// (their diagnostics are logged — the boot report). Called once by <c>WorldContentBootstrap</c>.
        /// </summary>
        Task LoadAndSpawnAsync(CancellationToken ct = default);

        /// <summary>
        /// Full, whole-or-nothing rebuild: stages and validates the whole corpus first (throwing
        /// <see cref="Hedron.Core.Systems.ContentLoadException"/> with the live world untouched on a
        /// shape error), then tears down world content and re-spawns areas and rooms, resolves the
        /// starting room and runs every <see cref="IContentReloadParticipant"/>. Returns the result
        /// counts that <c>ReloadCommand</c> wraps in <c>ContentReloadedEvent</c>, plus the
        /// participants' <see cref="ContentReloadResult.Diagnostics"/>. A refused reload runs no participant.
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
        int TemplatesRemoved)
    {
        private readonly IReadOnlyList<ContentDiagnostic>? _diagnostics;

        /// <summary>
        /// What the <see cref="IContentReloadParticipant"/>s reported while rebuilding (never a refusal).
        /// Empty — never null — including for <c>default(ContentReloadResult)</c>.
        /// </summary>
        public IReadOnlyList<ContentDiagnostic> Diagnostics
        {
            get => _diagnostics ?? Array.Empty<ContentDiagnostic>();
            init => _diagnostics = value;
        }
    }
}
