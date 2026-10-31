using Hedron.Core.ECS.Components;
using System.Collections.Generic;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.World.Systems;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Domain system for runtime room authoring. All methods mutate entity/component state only;
    /// event publication is the caller's responsibility.
    /// </summary>
    public interface IRoomBuilderSystem
    {
        RoomCreationResult CreateRoom(string name, string description = "", string areaId = "");
        void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional);
        void SetRoomName(uint roomId, string name);
        void SetRoomDescription(uint roomId, string description);

        /// <summary>
        /// True when <paramref name="roomId"/> is a live room carrying a <c>BlueprintComponent</c>
        /// with a registered <c>RoomTemplate</c> — the precondition for every spawn-rule mutation.
        /// <c>mkmob</c>/<c>mkitem</c> check it before creating anything, so a refusal leaves no orphan.
        /// </summary>
        bool TryGetAuthorableRoom(uint roomId, out string roomBlueprintId);

        /// <summary>
        /// Adds a spawn rule to the room. A blank <paramref name="ruleId"/> is minted
        /// (<see cref="SpawnRuleIds.Mint"/>). On success the registered <c>RoomTemplate</c>, the live
        /// <see cref="SpawnConfigComponent"/> and the spawn tracker (<c>ISpawnSystem.SyncRoom</c>)
        /// are all updated; on refusal nothing changes. Refuses when the room is not a registered
        /// room, the blueprint is unknown or is not a mob/item template, or the resulting rule set
        /// fails <see cref="SpawnRuleValidation"/>. INV-5: publishes nothing, writes no file.
        /// </summary>
        SpawnRuleMutationResult AddSpawnRule(uint roomId, string blueprintId, int count, int respawnDelaySeconds, string? ruleId = null);

        /// <summary>Updates the given fields of rule <paramref name="ruleId"/>; same effects and refusals as <see cref="AddSpawnRule"/>.</summary>
        SpawnRuleMutationResult UpdateSpawnRule(uint roomId, string ruleId, SpawnRuleUpdate update);

        /// <summary>
        /// Removes rule <paramref name="ruleId"/>. The live component is removed with the last rule
        /// (matching <c>RoomTemplate.Apply</c>); any live instances are detached, not despawned.
        /// </summary>
        SpawnRuleMutationResult RemoveSpawnRule(uint roomId, string ruleId);
    }

    /// <summary>Result of <see cref="IRoomBuilderSystem.CreateRoom"/>.</summary>
    public readonly record struct RoomCreationResult(uint RoomEntityId, string BlueprintId);

    /// <summary>Fields to change on a spawn rule; <c>null</c> leaves a field unchanged.</summary>
    public sealed record SpawnRuleUpdate(int? Count = null, int? RespawnDelaySeconds = null, string? BlueprintId = null);

    /// <summary>
    /// Outcome of a spawn-rule mutation. On success <see cref="Rule"/> is the rule as added,
    /// updated or removed, and <see cref="Sync"/> reports what the tracker spawned or detached.
    /// <see cref="Findings"/> carries the added/updated rule's out-of-designation finding, if any
    /// (advisory — the mutation still succeeded); <c>null</c> or empty means none, and a removal
    /// never carries one.
    /// </summary>
    public sealed record SpawnRuleMutationResult(
        bool Success,
        string? Error,
        SpawnRule? Rule,
        string RoomBlueprintId,
        SpawnSyncResult Sync,
        IReadOnlyList<PlacementFinding>? Findings = null)
    {
        public static SpawnRuleMutationResult Refused(string error, string roomBlueprintId = "") =>
            new(false, error, null, roomBlueprintId, SpawnSyncResult.None);
    }
}
