namespace Hedron.Core.ECS
{
    /// <summary>
    /// Marks an <see cref="IComponent"/> implementation as persistent — its state will be
    /// serialized to disk by <c>PersistenceSystem</c> and reloaded on next startup.
    /// An entity is persisted if it carries at least one <c>[Persistent]</c> component.
    /// Components without this attribute are transient and are rebuilt at runtime.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistentAttribute : Attribute { }
}
