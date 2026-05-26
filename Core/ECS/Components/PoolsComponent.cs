namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class PoolsComponent : IComponent
    {
        public int MaxHp { get; set; } = 100;
        public int CurrentHp { get; set; } = 100;
    }
}
