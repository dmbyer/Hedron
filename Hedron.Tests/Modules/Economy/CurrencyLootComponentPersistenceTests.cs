using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 4 — persistence guard for <see cref="CurrencyLootComponent"/>.
    ///
    /// Coverage contract (INV-23 / WP-2 plan):
    ///   <see cref="CurrencyLootComponent"/> is world content authored via YAML.
    ///   It must NEVER be persisted, even if the mob entity somehow carries
    ///   <c>PersistentEntity</c> (defense-in-depth: the component is not tagged [Persistent]).
    ///
    ///   Assert: after saving a mob entity that carries <see cref="CurrencyLootComponent"/>,
    ///   loading into a fresh world must show the component absent.
    /// </summary>
    public sealed class CurrencyLootComponentPersistenceTests
    {
        [Fact]
        public async Task CurrencyLootComponent_is_never_persisted_even_with_PersistentEntity()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // Simulate the worst case: an entity that somehow has both PersistentEntity
            // and CurrencyLootComponent. The component must still not appear in reload.
            var entity = ecs.CreateEntity().Id;
            ecs.AddComponent(entity, new PersistentEntity());
            ecs.AddComponent(entity, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (10, 50) }
            });

            // Confirm the component is present before saving.
            Assert.True(ecs.HasComponent<CurrencyLootComponent>(entity),
                "Precondition: CurrencyLootComponent must be present before save.");

            await harness.SaveAsync(entity);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.False(fresh.HasComponent<CurrencyLootComponent>(entity),
                "CurrencyLootComponent must NOT survive round-trip — it is not [Persistent] (INV-23).");
        }

        [Fact]
        public async Task Mob_entity_without_PersistentEntity_writes_no_row_for_CurrencyLootComponent()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // Typical mob: world content, no PersistentEntity.
            var mob = ecs.CreateEntity().Id;
            ecs.AddComponent(mob, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (1, 100) }
            });

            Assert.False(ecs.HasComponent<PersistentEntity>(mob),
                "Precondition: mob has no PersistentEntity (world content).");

            await harness.SaveAsync(mob);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.False(fresh.HasComponent<CurrencyLootComponent>(mob),
                "Mob (world content) must not persist — no PersistentEntity opt-in (INV-23).");
        }
    }
}
