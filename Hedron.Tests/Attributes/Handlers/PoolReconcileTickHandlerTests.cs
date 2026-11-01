using System;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Attributes.Handlers;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Attributes.Handlers
{
    /// <summary>
    /// Tier 2 — <see cref="PoolReconcileTickHandler"/> (combat-effective-scores T16): on each heartbeat
    /// it writes every pool's clamp back to storage, publishes nothing, and runs at Domain + 10.
    /// </summary>
    public sealed class PoolReconcileTickHandlerTests
    {
        [Fact]
        public async Task Heartbeat_writes_stored_current_above_cap_down_for_all_four_pools()
        {
            var ecs = new EntityService();
            var effects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var attributes = new AttributeSystem(ecs, effects, Options.Create(new DeathOptions()));
            var handler = new PoolReconcileTickHandler(attributes);
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes().WithPools(hp: 100, mana: 50, stamina: 50, astra: 10).Build();
            foreach (var score in new[] { ScoreId.HpMax, ScoreId.ManaMax, ScoreId.StaminaMax, ScoreId.AstraMax })
                TestEffects.ApplyStatModifier(effects, player, score, 20, durationSeconds: 5f);
            foreach (var pool in new[] { ResourceType.Hp, ResourceType.Mana, ResourceType.Stamina, ResourceType.Astra })
                attributes.RestoreToFraction(player, pool, 1.0);
            effects.AdvanceTick(TimeSpan.FromSeconds(10)); // every +Max buff expires

            await handler.HandleAsync(Ticks.At(1));

            var p = ecs.Get<PoolsComponent>(player);
            Assert.Equal((100, 50, 50, 10), (p.CurrentHp, p.CurrentMana, p.CurrentStamina, p.CurrentAstra));
        }

        [Fact]
        public void Priority_is_Domain_plus_10()
        {
            var handler = new PoolReconcileTickHandler(TestAttributes.Create(new EntityService()));
            Assert.Equal(HandlerPriority.Domain + 10, handler.Priority);
            Assert.True(handler.Priority > HandlerPriority.Domain && handler.Priority < HandlerPriority.Notification);
        }

        /// <summary>
        /// Publishes nothing and never consults death: its only collaborator is IAttributeSystem —
        /// no IEventBus, no IDeathSystem to call.
        /// </summary>
        [Fact]
        public void Depends_only_on_IAttributeSystem()
        {
            var parameters = typeof(PoolReconcileTickHandler).GetConstructors().Single().GetParameters();
            Assert.Equal(new[] { typeof(IAttributeSystem) }, parameters.Select(p => p.ParameterType));
        }
    }
}
