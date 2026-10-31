using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Editor-side authoring of room spawn rules — the single spawn truth for mobs and found items.
    /// Implemented by <see cref="ContentDefinitionCatalog"/> so rule writes serialize with every other
    /// catalog write (its write gate). File-only: a saved rule reaches the live game world on Server
    /// <c>reload</c> or restart (INV-8, INV-22/23).
    /// </summary>
    public interface ISpawnRuleAuthoring
    {
        /// <summary>
        /// Every <c>(room, rule)</c> whose rule names <paramref name="blueprintId"/>, ordered by room
        /// blueprint id (ordinal). Empty for a kind other than mob or item. Lock-free read over the
        /// catalog's cached room sweep.
        /// </summary>
        IReadOnlyList<SpawnSite> SpawnSitesFor(ContentKind kind, string blueprintId);

        /// <summary>
        /// Adds <paramref name="rule"/> to the room, or replaces the room's rule with the same id
        /// (case-insensitive). A blank <see cref="SpawnRule.Id"/> is minted
        /// (<see cref="SpawnRuleIds.Mint"/>). Refuses — writing nothing — an unknown room or a rule
        /// set that fails <see cref="SpawnRuleValidation"/>.
        /// </summary>
        Task<ContentWriteResult> UpsertSpawnRuleAsync(string roomBlueprintId, SpawnRule rule, CancellationToken ct = default);

        /// <summary>Removes the room's rule <paramref name="ruleId"/>; refuses an unknown room or rule.</summary>
        Task<ContentWriteResult> RemoveSpawnRuleAsync(string roomBlueprintId, string ruleId, CancellationToken ct = default);
    }
}
