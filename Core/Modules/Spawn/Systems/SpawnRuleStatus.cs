namespace Hedron.Core.Modules.Spawn.Systems
{
    /// <summary>
    /// One rule's tracker status, from <see cref="ISpawnSystem.DescribeRoom"/>.
    /// <paramref name="NextRespawnAt"/> is the earliest pending respawn among its vacant slots,
    /// or <c>null</c> when none is pending.
    /// </summary>
    public sealed record SpawnRuleStatus(
        string RuleId,
        string BlueprintId,
        int Count,
        int Live,
        int RespawnDelaySeconds,
        DateTime? NextRespawnAt);
}
