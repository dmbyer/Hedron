using System;
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
    /// Why <see cref="IAreaLayoutSystem.ConnectAsync"/> did or did not write an exit. The three
    /// non-success outcomes are distinct because the grid editor treats them differently: only
    /// <see cref="WriteFailed"/> is an error worth surfacing — the other two mean "the click was not
    /// a connect gesture", which the editor answers by moving the selection instead.
    /// </summary>
    public enum AreaConnectOutcome
    {
        /// <summary>The exit (and, per policy, its inverse) was written.</summary>
        Connected,

        /// <summary>One of the two blueprint ids has no room definition on disk.</summary>
        RoomNotFound,

        /// <summary>
        /// The two rooms do not occupy orthogonally adjacent grid cells (including: not placed at
        /// all, the same cell, or laid out in different areas), so no direction describes the pair.
        /// </summary>
        NotAdjacent,

        /// <summary>The write was attempted and the catalog refused it.</summary>
        WriteFailed,
    }

    /// <summary>
    /// Result of <see cref="IAreaLayoutSystem.ConnectAsync"/>. <see cref="Direction"/> is the
    /// direction written, non-null only when <see cref="Outcome"/> is
    /// <see cref="AreaConnectOutcome.Connected"/>.
    /// </summary>
    public sealed record AreaConnectResult(
        AreaConnectOutcome Outcome,
        Direction? Direction,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public bool Connected => Outcome == AreaConnectOutcome.Connected;

        public static AreaConnectResult Rejected(AreaConnectOutcome outcome) =>
            new(outcome, null, Array.Empty<string>(), Array.Empty<string>());
    }

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

        /// <summary>
        /// Connects two rooms that occupy adjacent grid cells: derives the direction from their laid-out
        /// positions (authored coordinates or, for a coordless room, its position in this area's
        /// <see cref="Propose"/> proposal), writes that exit on
        /// <paramref name="fromRoomBlueprintId"/>, and writes the inverse exit on
        /// <paramref name="toRoomBlueprintId"/> — the grid's connect gesture is always bidirectional,
        /// under <c>SaveRoomAsync</c>'s existing conflict policy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole connect <em>policy</em> lives here — which direction pair to write, that the write
        /// is bidirectional, and that a non-adjacent pair is refused rather than approximated. Only the
        /// direction arithmetic is delegated (to <c>DirectionExtensions.FromOffset</c>). A refusal is a
        /// result, never an exception: <see cref="AreaConnectOutcome.NotAdjacent"/> means "these two
        /// rooms are not a connectable pair", which the grid editor renders as a selection change, not
        /// an error.
        /// </para>
        /// <para>
        /// Adjacency is judged in all three axes, so two rooms on different Z layers are never
        /// connected as though they were on one — the layer difference is part of the offset
        /// <c>FromOffset</c> inverts, and a room directly above another yields <c>Up</c>.
        /// </para>
        /// </remarks>
        Task<AreaConnectResult> ConnectAsync(
            string fromRoomBlueprintId,
            string toRoomBlueprintId,
            CancellationToken ct = default);
    }
}
