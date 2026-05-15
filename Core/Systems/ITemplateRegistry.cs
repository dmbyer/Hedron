using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Cross-cutting registry of authored <see cref="IEntityTemplate"/>s, keyed by stable
    /// blueprint id. Lives in <c>Core/Systems/</c> because every content-bearing module
    /// (world, mobs, items, shops) registers into the same registry.
    /// </summary>
    /// <remarks>
    /// <b>Spawn semantics.</b> <see cref="Spawn(string)"/> allocates a new <see cref="Entity"/>
    /// via <see cref="EntityService.CreateEntity"/>, attaches a <c>BlueprintComponent</c>
    /// recording the blueprint id, and invokes <see cref="IEntityTemplate.Apply"/> to add
    /// the archetype's components. No events are published — runtime callers (admin <c>@spawn</c>)
    /// publish their own past-tense events.
    /// </remarks>
    public interface ITemplateRegistry
    {
        void Register(string blueprintId, IEntityTemplate template);
        bool TryGet(string blueprintId, [NotNullWhen(true)] out IEntityTemplate? template);
        Entity Spawn(string blueprintId);
        Entity Spawn(string blueprintId, IDictionary<string, object>? overrides);
        IReadOnlyCollection<string> AllBlueprintIds();
        void Clear();
    }
}
