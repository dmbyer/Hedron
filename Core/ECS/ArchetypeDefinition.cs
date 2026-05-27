using System;
using System.Collections.Generic;

namespace Hedron.Core.ECS
{
    /// <summary>
    /// Declares the required and optional component types for one <see cref="EntityArchetype"/>.
    /// The definition is declarative — it describes what a well-formed entity looks like;
    /// it is never used to construct entities. See <c>docs/reference/archetypes.md</c>.
    /// </summary>
    public sealed class ArchetypeDefinition
    {
        public EntityArchetype Archetype { get; init; }

        /// <summary>Component types that MUST be present for this archetype to be valid.</summary>
        public IReadOnlyList<Type> Required { get; init; } = Array.Empty<Type>();

        /// <summary>Component types that MAY be present but are not required for validation.</summary>
        public IReadOnlyList<Type> Optional { get; init; } = Array.Empty<Type>();
    }
}
