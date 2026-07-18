using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Systems;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>One room's grid position — a plain point, not a runtime component.</summary>
    public sealed record RoomPosition(int X, int Y, int Z);

    /// <summary>
    /// Result of <see cref="IAreaLayoutSystem.Propose"/>: the area's already-anchored rooms
    /// (never moved), a deterministic proposal for every coordless room, and any coordinate
    /// collisions found among the anchored rooms.
    /// </summary>
    public sealed record AreaLayoutProposal(
        IReadOnlyDictionary<string, RoomPosition> Anchored,
        IReadOnlyDictionary<string, RoomPosition> Proposed,
        IReadOnlyList<CoordinateCollision> Collisions);

    /// <summary>Result of <see cref="IAreaLayoutSystem.ApplyProposalAsync"/>.</summary>
    public sealed record AreaLayoutApplyResult(int Written, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Deterministic auto-layout for an area's rooms that lack authored <c>X/Y/Z</c> coordinates —
    /// the visual grid editor's ghost-cell proposal and its "Apply layout" bulk write. Pure
    /// computation: never publishes (INV-5), no RNG or wall-clock (INV-26 moot — nothing to seed).
    /// </summary>
    public interface IAreaLayoutSystem
    {
        /// <summary>
        /// Computes a layout proposal for <paramref name="areaBlueprintId"/> from the current
        /// on-disk state via the catalog. Never writes. Deterministic: the same disk state always
        /// yields the same proposal. Anchored (fully-coordinate) rooms are never moved.
        /// </summary>
        AreaLayoutProposal Propose(string areaBlueprintId);

        /// <summary>
        /// Re-derives the proposal from disk and writes coordinates for every room that still
        /// lacks them (previously-anchored rooms are never rewritten). Best-effort: a failure on
        /// one room is recorded as a warning and does not stop the rest.
        /// </summary>
        Task<AreaLayoutApplyResult> ApplyProposalAsync(string areaBlueprintId, CancellationToken ct = default);
    }
}
