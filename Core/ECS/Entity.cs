namespace Hedron.Core.ECS
{
    /// <summary>
    /// Identity wrapper around an entity id.
    /// </summary>
    /// <remarks>
    /// The <see cref="uint"/> id is authoritative; <c>Entity</c> is call-site flavour and has
    /// no independent lifetime. Component-to-component references are stored as <c>uint</c>.
    /// See <c>docs/architecture/02-ecs.md</c>.
    /// </remarks>
    public readonly record struct Entity(uint Id);
}
