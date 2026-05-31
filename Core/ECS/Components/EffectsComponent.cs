using System.Collections.Generic;
using System.Text.Json.Serialization;
using Hedron.Core.Modules.Effects;

namespace Hedron.Core.ECS.Components
{
    [Persistent]
    [JsonConverter(typeof(EffectsComponentJsonConverter))]
    public sealed class EffectsComponent : IComponent
    {
        public List<Effect> Effects { get; set; } = new();
    }
}
