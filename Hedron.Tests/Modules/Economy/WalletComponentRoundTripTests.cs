using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 4 — persistence round-trip tests for <see cref="WalletComponent"/>.
    ///
    /// Coverage contract:
    ///   - WalletComponent save → load into fresh world → balances equal (by enum name, not ordinal).
    ///   - Dictionary key is serialized as the enum NAME ("Coin"), not the ordinal (0), so a future
    ///     CurrencyId reordering cannot corrupt saved wallets.
    /// </summary>
    public sealed class WalletComponentRoundTripTests
    {
        // ── Test 1: WalletComponent survives save→load with correct balances ─────

        [Fact]
        public async Task WalletComponent_balances_survive_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var entity = ecs.CreateEntity().Id;
            ecs.AddComponent(entity, new WalletComponent
            {
                Balances = { [CurrencyId.Coin] = 250L },
            });
            ecs.AddComponent(entity, new PersistentEntity());

            await harness.SaveAsync(entity);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<WalletComponent>(entity),
                "WalletComponent must survive round-trip (INV-14).");

            var wallet = fresh.Get<WalletComponent>(entity);
            Assert.True(wallet.Balances.ContainsKey(CurrencyId.Coin),
                "CurrencyId.Coin key must be present after reload.");
            Assert.Equal(250L, wallet.Balances[CurrencyId.Coin]); // Balance must equal the saved value after round-trip.
        }

        // ── Test 2: Zero balance entry round-trips ────────────────────────────────

        [Fact]
        public async Task WalletComponent_zero_balance_entry_round_trips()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var entity = ecs.CreateEntity().Id;
            ecs.AddComponent(entity, new WalletComponent
            {
                Balances = { [CurrencyId.Coin] = 0L },
            });
            ecs.AddComponent(entity, new PersistentEntity());

            await harness.SaveAsync(entity);

            var fresh = await harness.ReloadIntoFreshWorld();

            var wallet = fresh.Get<WalletComponent>(entity);
            Assert.Equal(0L, wallet.Balances[CurrencyId.Coin]);
        }

        // ── Test 3: Enum key is serialized by name, not ordinal ──────────────────

        /// <summary>
        /// Asserts that the serialized JSON for <see cref="WalletComponent"/> represents
        /// the <see cref="CurrencyId"/> dictionary key as the enum NAME ("Coin"), not as the
        /// integer ordinal ("0"). This is the serialization-shape guarantee: if a future
        /// <see cref="CurrencyId"/> value is inserted before <c>Coin</c>, the persisted name
        /// "Coin" still resolves correctly, whereas ordinal "0" would silently map to the new
        /// first member.
        /// </summary>
        [Fact]
        public void WalletComponent_CurrencyId_dictionary_key_is_serialized_as_enum_name()
        {
            // Use the same JsonSerializerOptions that ComponentSerializer uses.
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                Converters = { new JsonStringEnumConverter() },
            };

            var component = new WalletComponent();
            component.Balances[CurrencyId.Coin] = 100L;

            var json = JsonSerializer.Serialize(component, options);

            // The key must appear as the string "Coin", not "0".
            // xUnit's Assert.Contains(string, string) searches for a substring.
            Assert.Contains("\"Coin\"", json);  // CurrencyId.Coin must be serialized as the enum name 'Coin'.
            Assert.DoesNotContain("\"0\"", json); // Ordinal-based keys ('\"0\"') must not appear.
        }

        // ── Test 4: Entity without PersistentEntity writes no row ─────────────────

        [Fact]
        public async Task WalletComponent_without_PersistentEntity_writes_no_row()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // No PersistentEntity — entity is world-content (or an un-opted-in holder).
            var entity = ecs.CreateEntity().Id;
            ecs.AddComponent(entity, new WalletComponent
            {
                Balances = { [CurrencyId.Coin] = 500L },
            });

            await harness.SaveAsync(entity);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.False(fresh.HasComponent<WalletComponent>(entity),
                "WalletComponent must not be persisted when entity lacks PersistentEntity (INV-14).");
        }
    }
}
