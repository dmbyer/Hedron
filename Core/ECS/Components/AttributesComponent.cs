namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class AttributesComponent : IComponent
    {
        public int Level { get; set; } = 1;
        public int Strength { get; set; } = 10;
        public int Dexterity { get; set; } = 10;
        public int Constitution { get; set; } = 10;
    }
}
