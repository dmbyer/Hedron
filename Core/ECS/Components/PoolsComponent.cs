namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class PoolsComponent : IComponent
    {
        public int MaxHp { get; set; } = 100;
        public int CurrentHp { get; set; } = 100;
        public int MaxMana { get; set; } = 50;
        public int CurrentMana { get; set; } = 50;
        public int MaxStamina { get; set; } = 50;
        public int CurrentStamina { get; set; } = 50;
        public int MaxAstra { get; set; } = 10;
        public int CurrentAstra { get; set; } = 10;
    }
}
