using System.Collections.Generic;
using System.Text.Json.Serialization;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;

namespace Hedron.Core.ECS.Components
{
    [Persistent]
    [JsonConverter(typeof(AbilitiesComponentJsonConverter))]
    public sealed class AbilitiesComponent : IComponent
    {
        public List<string> Known { get; set; } = new();
        public Dictionary<string, float> CooldownRemaining { get; set; } = new();
    }
}
