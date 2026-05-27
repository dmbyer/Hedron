namespace Hedron.Core.ECS.Components
{
    [Flags]
    public enum EntityStateFlags
    {
        None          = 0,
        InCombat      = 1 << 0,   // 1
        Resting       = 1 << 1,   // 2
        Incapacitated = 1 << 2,   // 4
    }

    public class EntityStateComponent : IComponent
    {
        public EntityStateFlags ActiveStates { get; set; }
    }
}
