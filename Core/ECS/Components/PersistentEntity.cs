using Hedron.Core.ECS;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Zero-data marker component that opts an entity into persistence.
    /// Entities without this component are never written to disk regardless of which
    /// <c>[Persistent]</c>-tagged component types they carry.
    /// Tagged <c>[Persistent]</c> itself so it round-trips through the snapshot and
    /// hydrated entities automatically re-acquire their opt-in marker.
    /// </summary>
    [Persistent]
    public sealed class PersistentEntity : IComponent { }
}
