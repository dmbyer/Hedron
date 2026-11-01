using System;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Microsoft.Extensions.Options;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Builds a real <see cref="AttributeSystem"/> over an <see cref="EffectSystem"/> with no
    /// contributors — for tests whose system under test now takes an <see cref="IAttributeSystem"/>
    /// only to write pools through the effective-cap seam.
    /// </summary>
    public static class TestAttributes
    {
        public static AttributeSystem Create(EntityService ecs, int hpFloor = -10)
            => new(ecs, new EffectSystem(ecs, Array.Empty<IEffectContributor>()),
                Options.Create(new DeathOptions { HpFloor = hpFloor }));
    }
}
