using Hedron.Core.ECS;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Provides a reflection-built map of every <see cref="IComponent"/> implementor,
    /// recording which types carry <c>[PersistentAttribute]</c>.
    /// Built once at startup; immutable thereafter.
    /// </summary>
    public interface IComponentTypeRegistry
    {
        /// <summary>Returns <c>true</c> if <paramref name="componentType"/> is tagged <c>[Persistent]</c>.</summary>
        bool IsPersistent(Type componentType);

        /// <summary>
        /// Resolves a component type by its full name (as stored in entity snapshots).
        /// Returns <c>null</c> if the name is unknown.
        /// </summary>
        Type? Resolve(string typeName);

        /// <summary>Returns all component types tagged <c>[Persistent]</c>.</summary>
        IReadOnlyList<Type> AllPersistentTypes();
    }
}
