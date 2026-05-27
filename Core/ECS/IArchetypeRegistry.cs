using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Validation and detection gateway for entity archetypes.
    /// Never used for entity construction — entities are built by domain systems via
    /// <see cref="EntityService"/> and <see cref="Hedron.Core.Systems.ITemplateRegistry"/>.
    /// See <c>docs/architecture/02-ecs.md</c> and <c>docs/reference/archetypes.md</c>.
    /// </summary>
    public interface IArchetypeRegistry
    {
        /// <summary>Returns the component types that must be present for <paramref name="archetype"/>.</summary>
        IReadOnlyList<Type> RequiredComponents(EntityArchetype archetype);

        /// <summary>Returns the component types that may optionally be present for <paramref name="archetype"/>.</summary>
        IReadOnlyList<Type> OptionalComponents(EntityArchetype archetype);

        /// <summary>
        /// Returns true when the entity has every required component for <paramref name="expected"/>.
        /// Does not care about optional components.
        /// </summary>
        bool Validate(uint entityId, EntityArchetype expected);

        /// <summary>
        /// Infers the best-matching archetype by inspecting the entity's component set.
        /// Prefer <c>HasComponent&lt;T&gt;</c> queries in handler/system code; use
        /// <c>Detect</c> only for generic tooling or debug inspection.
        /// Returns <see cref="EntityArchetype.Custom"/> when no standard archetype matches.
        /// </summary>
        EntityArchetype Detect(uint entityId);

        /// <summary>
        /// Yields the required component types that are absent from <paramref name="entityId"/>
        /// for the given <paramref name="archetype"/>. Empty when the entity is fully valid.
        /// </summary>
        IEnumerable<Type> MissingRequired(uint entityId, EntityArchetype archetype);
    }
}
