namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class AttributesComponent : IComponent
    {
        public int Level { get; set; } = 1;
        public int Mind { get; set; } = 10;
        public int Body { get; set; } = 10;
        public int Spirit { get; set; } = 10;
        public int Attunement { get; set; } = 10;
    }
}
