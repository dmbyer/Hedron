namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// Records the authored blueprint id this entity was spawned from.
    /// Lets <c>WorldContentLoader</c> skip-on-conflict during reseed and lets admin tooling
    /// distinguish authored entities from ad-hoc ones.
    /// </summary>
    [Persistent]
    public class BlueprintComponent : IComponent
    {
        public string BlueprintId { get; set; } = string.Empty;
    }
}
