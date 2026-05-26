using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    [Persistent]
    public sealed class MobDataComponent : IComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;
    }
}
