using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Declared-edge reference model over the on-disk YAML definition set. Answers three read
    /// questions without applying any policy: <em>does this target exist?</em>, <em>who points at
    /// this id?</em>, and <em>what is broken across all definitions?</em> Pure read — returns
    /// structured results, publishes nothing, holds no live entities (INV-5).
    /// </summary>
    /// <remarks>
    /// The declared edge set is:
    /// <list type="bullet">
    ///   <item><c>(Room, AreaId) → Area</c></item>
    ///   <item><c>(Room, Exits[dir]) → Room</c></item>
    ///   <item><c>(Item, SpawnRoomBlueprintId) → Room</c></item>
    ///   <item><c>(Mob, SpawnRoomBlueprintId) → Room</c></item>
    ///   <item><c>(Area, Rooms[]) → Room</c></item>
    ///   <item><c>(Room, SpawnRules[].BlueprintId) → Mob or Item</c> — two-kind target; resolves
    ///   against either kind (an id present as a mob file <em>or</em> an item file counts as
    ///   resolved; broken only when it matches neither).</item>
    /// </list>
    /// Adding a new edge requires only a new <see cref="ReferenceEdge"/> declaration — no
    /// additional code paths needed (INV-19).
    /// </remarks>
    public interface IContentReferenceIndex
    {
        /// <summary>
        /// Returns <c>true</c> if a definition file for the given <paramref name="targetKind"/>
        /// and <paramref name="targetBlueprintId"/> exists on disk; <c>false</c> otherwise.
        /// </summary>
        bool Resolves(ContentKind targetKind, string targetBlueprintId);

        /// <summary>
        /// Returns every definition that references <paramref name="targetBlueprintId"/> as a
        /// target of <paramref name="targetKind"/>, described as the cascade-clear edit the WP2
        /// delete path would apply to drop the dangling link.
        /// </summary>
        IReadOnlyList<ReferrerEdit> Referrers(ContentKind targetKind, string targetBlueprintId);

        /// <summary>
        /// Sweeps the entire on-disk definition set and returns every edge whose target does not
        /// resolve. Used by the integrity/health page to list all broken links at once.
        /// </summary>
        IReadOnlyList<BrokenReference> SweepBroken();

        /// <summary>
        /// Returns the broken references in one in-memory definition — the dangling refs the
        /// warn-but-allow <c>SaveAsync</c> surfaces as non-blocking warnings before writing.
        /// </summary>
        IReadOnlyList<BrokenReference> BrokenFor(IEntityTemplate definition);
    }
}
