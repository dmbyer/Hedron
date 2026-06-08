using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Account
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AccountSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/account-and-character.md.
    ///   - CreateAccountAsync: entity created, AccountComponent stamped with correct
    ///     username (lowercased) and CreatedAtUtc from the injected clock.
    ///   - CreateAccountAsync: PersistentEntity added.
    ///   - CreateCharacterAsync: entity created, CharacterComponent stamped with
    ///     clock time for CreatedAtUtc and LastLoginUtc.
    ///   - CreateCharacterAsync: required components attached (Location, Inventory,
    ///     Equipment, Attributes, Pools, Respawn, AspectAffinities, PersistentEntity).
    ///   - CreateCharacterAsync: character id registered on the account's roster.
    ///   - RecordLogout: updates LastLoginUtc on the CharacterComponent via the clock.
    ///   - AuthenticateAsync: correct credentials succeed; wrong credentials fail.
    ///   - UsernameExists / CharacterNameExists: index reflects created entities.
    ///   - GetCharacterList: returns roster with correct ids and names.
    ///   - Index is case-insensitive for usernames.
    /// </summary>
    public sealed class AccountSystemTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hand-rolled stub <see cref="IPasswordHasher"/> — stores password as-is and
        /// compares literally. Avoids the 100 000-iteration PBKDF2 cost in unit tests.
        /// </summary>
        private sealed class PlaintextHasher : IPasswordHasher
        {
            public string Hash(string password) => "plain:" + password;
            public bool Verify(string password, string hash) => hash == "plain:" + password;
        }

        /// <summary>
        /// Hand-rolled stub <see cref="IAbilitySystem"/>. Only <see cref="Learn"/> is
        /// called by <see cref="AccountSystem.CreateCharacterAsync"/>; all other members
        /// return safe defaults.
        /// </summary>
        private sealed class NullAbilitySystem : IAbilitySystem
        {
            public bool Learn(uint entityId, string abilityId) => true;
            public bool Teach(uint t, uint s, string a) => false;
            public bool IsKnown(uint e, string a) => false;
            public bool IsOffensive(string a) => false;
            public IReadOnlyList<string> GetKnown(uint e) => Array.Empty<string>();
            public float GetCooldownRemaining(uint e, string a) => 0f;
            public IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint e)
                => Array.Empty<(string, float)>();
            public void AdvanceCooldowns(TimeSpan elapsed) { }
            public AbilityActivationResult Activate(uint a, string id, uint? t = null, bool ext = false)
                => new(AbilityActivationOutcome.UnknownAbility, id,
                       Array.Empty<Hedron.Core.Modules.Effects.Effect>(),
                       Array.Empty<Hedron.Core.Modules.Abilities.ResourceCost>(),
                       0f);
        }

        // ── Builder helpers ───────────────────────────────────────────────────────

        private static readonly DateTime BaseTime =
            new DateTime(2025, 3, 15, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Wires a fresh <see cref="AccountSystem"/> with a <see cref="FakeClock"/>
        /// seeded to <see cref="BaseTime"/> and a <see cref="WorldConfiguration"/>
        /// pointing at <paramref name="startingRoomId"/>.
        /// </summary>
        private static (AccountSystem system, EntityService ecs, FakeClock clock)
            Build(uint startingRoomId = 0u, IConfiguration? config = null)
        {
            var ecs = new EntityService();
            var clock = new FakeClock(BaseTime);
            var worldConfig = new WorldConfiguration
            {
                StartingRoomEntityId = startingRoomId,
                StartingRoomBlueprintId = startingRoomId == 0u ? null : startingRoomId.ToString(),
            };

            config ??= new ConfigurationBuilder().Build();

            var system = new AccountSystem(
                ecs,
                new PlaintextHasher(),
                worldConfig,
                config,
                new NullAbilitySystem(),
                clock,
                NullLogger<AccountSystem>.Instance);

            return (system, ecs, clock);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // CreateAccountAsync
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CreateAccountAsync_returns_nonzero_entity_id()
        {
            var (system, _, _) = Build();

            var id = await system.CreateAccountAsync("Alice", "p@ssw0rd");

            Assert.NotEqual(0u, id);
        }

        [Fact]
        public async Task CreateAccountAsync_attaches_AccountComponent_with_normalized_username()
        {
            var (system, ecs, _) = Build();

            var id = await system.CreateAccountAsync("ALICE", "p@ssw0rd");

            Assert.True(ecs.HasComponent<AccountComponent>(id));
            var account = ecs.Get<AccountComponent>(id);
            Assert.Equal("alice", account.Username);
        }

        [Fact]
        public async Task CreateAccountAsync_stamps_CreatedAtUtc_from_clock()
        {
            var (system, ecs, clock) = Build();
            var expected = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            clock.UtcNow = expected;

            var id = await system.CreateAccountAsync("bob", "s3cr3t");

            var account = ecs.Get<AccountComponent>(id);
            Assert.Equal(expected, account.CreatedAtUtc);
        }

        [Fact]
        public async Task CreateAccountAsync_attaches_PersistentEntity()
        {
            var (system, ecs, _) = Build();

            var id = await system.CreateAccountAsync("carol", "pw");

            Assert.True(ecs.HasComponent<PersistentEntity>(id));
        }

        [Fact]
        public async Task CreateAccountAsync_hashes_password_on_AccountComponent()
        {
            var (system, ecs, _) = Build();

            var id = await system.CreateAccountAsync("dave", "mypassword");

            var account = ecs.Get<AccountComponent>(id);
            // PlaintextHasher prefixes "plain:" — confirm it was called, not stored raw
            Assert.StartsWith("plain:", account.PasswordHash, StringComparison.Ordinal);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // UsernameExists / CharacterNameExists — index
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task UsernameExists_returns_true_after_account_created()
        {
            var (system, _, _) = Build();
            await system.CreateAccountAsync("eve", "pw");

            Assert.True(system.UsernameExists("eve"));
        }

        [Fact]
        public async Task UsernameExists_is_case_insensitive()
        {
            var (system, _, _) = Build();
            await system.CreateAccountAsync("Frank", "pw");

            Assert.True(system.UsernameExists("FRANK"));
            Assert.True(system.UsernameExists("frank"));
        }

        [Fact]
        public void UsernameExists_returns_false_for_unknown_username()
        {
            var (system, _, _) = Build();

            Assert.False(system.UsernameExists("nobody"));
        }

        [Fact]
        public async Task CharacterNameExists_returns_true_after_character_created()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("grace", "pw");
            await system.CreateCharacterAsync(accountId, "Gandalf");

            Assert.True(system.CharacterNameExists("Gandalf"));
        }

        [Fact]
        public async Task CharacterNameExists_is_case_insensitive()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("henry", "pw");
            await system.CreateCharacterAsync(accountId, "Aragorn");

            Assert.True(system.CharacterNameExists("aragorn"));
            Assert.True(system.CharacterNameExists("ARAGORN"));
        }

        [Fact]
        public void CharacterNameExists_returns_false_for_unknown_name()
        {
            var (system, _, _) = Build();

            Assert.False(system.CharacterNameExists("NoSuchHero"));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // AuthenticateAsync
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task AuthenticateAsync_succeeds_with_correct_credentials()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("ivan", "correctpw");

            var result = await system.AuthenticateAsync("ivan", "correctpw");

            Assert.True(result.Success);
            Assert.Equal(accountId, result.AccountEntityId);
        }

        [Fact]
        public async Task AuthenticateAsync_fails_with_wrong_password()
        {
            var (system, _, _) = Build();
            await system.CreateAccountAsync("julia", "rightpw");

            var result = await system.AuthenticateAsync("julia", "wrongpw");

            Assert.False(result.Success);
            Assert.Equal(0u, result.AccountEntityId);
        }

        [Fact]
        public async Task AuthenticateAsync_fails_for_unknown_username()
        {
            var (system, _, _) = Build();

            var result = await system.AuthenticateAsync("nobody", "pw");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task AuthenticateAsync_is_case_insensitive_for_username()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("Karl", "pw");

            var result = await system.AuthenticateAsync("KARL", "pw");

            Assert.True(result.Success);
            Assert.Equal(accountId, result.AccountEntityId);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // CreateCharacterAsync
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CreateCharacterAsync_returns_nonzero_entity_id()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("laura", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Legolas");

            Assert.NotEqual(0u, charId);
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_CharacterComponent_with_name_and_account()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("mike", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Gimli");

            Assert.True(ecs.HasComponent<CharacterComponent>(charId));
            var character = ecs.Get<CharacterComponent>(charId);
            Assert.Equal("Gimli", character.CharacterName);
            Assert.Equal(accountId, character.AccountEntityId);
        }

        [Fact]
        public async Task CreateCharacterAsync_stamps_CreatedAtUtc_from_clock()
        {
            var (system, ecs, clock) = Build();
            var expected = new DateTime(2025, 9, 20, 8, 30, 0, DateTimeKind.Utc);
            clock.UtcNow = expected;
            var accountId = await system.CreateAccountAsync("nancy", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Frodo");

            var character = ecs.Get<CharacterComponent>(charId);
            Assert.Equal(expected, character.CreatedAtUtc);
        }

        [Fact]
        public async Task CreateCharacterAsync_stamps_LastLoginUtc_from_clock_at_creation_time()
        {
            var (system, ecs, clock) = Build();
            var expected = new DateTime(2025, 9, 20, 8, 30, 0, DateTimeKind.Utc);
            clock.UtcNow = expected;
            var accountId = await system.CreateAccountAsync("oliver", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Bilbo");

            var character = ecs.Get<CharacterComponent>(charId);
            Assert.Equal(expected, character.LastLoginUtc);
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_LocationComponent()
        {
            var (system, ecs, _) = Build(startingRoomId: 42u);
            var accountId = await system.CreateAccountAsync("petra", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Saruman");

            Assert.True(ecs.HasComponent<LocationComponent>(charId));
            var loc = ecs.Get<LocationComponent>(charId);
            Assert.Equal(42u, loc.RoomEntityId);
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_InventoryComponent()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("quinn", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Boromir");

            Assert.True(ecs.HasComponent<InventoryComponent>(charId));
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_EquipmentComponent()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("rachel", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Faramir");

            Assert.True(ecs.HasComponent<EquipmentComponent>(charId));
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_AttributesComponent_with_defaults()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("sam", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Eowyn");

            Assert.True(ecs.HasComponent<AttributesComponent>(charId));
            var attrs = ecs.Get<AttributesComponent>(charId);
            // CharacterDefaultsOptions.AttributeDefault = 10
            Assert.Equal(10, attrs.Mind);
            Assert.Equal(10, attrs.Body);
            Assert.Equal(10, attrs.Spirit);
            Assert.Equal(10, attrs.Attunement);
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_PoolsComponent_with_max_values_set()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("tara", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Theoden");

            Assert.True(ecs.HasComponent<PoolsComponent>(charId));
            var pools = ecs.Get<PoolsComponent>(charId);
            // CharacterDefaultsOptions defaults: MaxHp=100, MaxMana=50, MaxStamina=50, MaxAstra=10
            Assert.Equal(100, pools.MaxHp);
            Assert.Equal(100, pools.CurrentHp);
            Assert.Equal(50, pools.MaxMana);
            Assert.Equal(50, pools.CurrentMana);
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_PersistentEntity()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("uma", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Elrond");

            Assert.True(ecs.HasComponent<PersistentEntity>(charId));
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_RespawnComponent()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("vera", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Celeborn");

            Assert.True(ecs.HasComponent<RespawnComponent>(charId));
        }

        [Fact]
        public async Task CreateCharacterAsync_attaches_AspectAffinitiesComponent()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("will", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Galadriel");

            Assert.True(ecs.HasComponent<AspectAffinitiesComponent>(charId));
        }

        [Fact]
        public async Task CreateCharacterAsync_registers_character_on_account_roster()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("xena", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Glorfindel");

            var account = ecs.Get<AccountComponent>(accountId);
            Assert.Contains(charId, account.CharacterEntityIds);
        }

        [Fact]
        public async Task CreateCharacterAsync_multiple_characters_all_registered_on_account()
        {
            var (system, ecs, _) = Build();
            var accountId = await system.CreateAccountAsync("yvonne", "pw");

            var charId1 = await system.CreateCharacterAsync(accountId, "Gloin");
            var charId2 = await system.CreateCharacterAsync(accountId, "Oin");

            var account = ecs.Get<AccountComponent>(accountId);
            Assert.Contains(charId1, account.CharacterEntityIds);
            Assert.Contains(charId2, account.CharacterEntityIds);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GetCharacterList
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetCharacterList_returns_empty_for_account_with_no_characters()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("zoe", "pw");

            var list = system.GetCharacterList(accountId);

            Assert.Empty(list);
        }

        [Fact]
        public async Task GetCharacterList_returns_summary_for_each_created_character()
        {
            var (system, _, _) = Build();
            var accountId = await system.CreateAccountAsync("alpha", "pw");
            var charId1 = await system.CreateCharacterAsync(accountId, "Thorin");
            var charId2 = await system.CreateCharacterAsync(accountId, "Kili");

            var list = system.GetCharacterList(accountId);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, s => s.CharacterEntityId == charId1 && s.CharacterName == "Thorin");
            Assert.Contains(list, s => s.CharacterEntityId == charId2 && s.CharacterName == "Kili");
        }

        [Fact]
        public void GetCharacterList_returns_empty_for_unknown_account_id()
        {
            var (system, _, _) = Build();

            var list = system.GetCharacterList(99999u);

            Assert.Empty(list);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // RecordLogout
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task RecordLogout_updates_LastLoginUtc_to_clock_time()
        {
            var (system, ecs, clock) = Build();
            var accountId = await system.CreateAccountAsync("beta", "pw");
            var charId = await system.CreateCharacterAsync(accountId, "Balin");

            // Advance clock and record logout
            var logoutTime = BaseTime + TimeSpan.FromHours(3);
            clock.UtcNow = logoutTime;
            system.RecordLogout(charId);

            var character = ecs.Get<CharacterComponent>(charId);
            Assert.Equal(logoutTime, character.LastLoginUtc);
        }

        [Fact]
        public async Task RecordLogout_uses_exact_clock_timestamp()
        {
            var (system, ecs, clock) = Build();
            var accountId = await system.CreateAccountAsync("gamma", "pw");
            var charId = await system.CreateCharacterAsync(accountId, "Dwalin");

            // Record logout at a precisely controlled time
            var t1 = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            clock.UtcNow = t1;
            system.RecordLogout(charId);
            Assert.Equal(t1, ecs.Get<CharacterComponent>(charId).LastLoginUtc);

            // Advance and record again — must reflect new time
            clock.Advance(TimeSpan.FromSeconds(1));
            system.RecordLogout(charId);
            Assert.Equal(t1 + TimeSpan.FromSeconds(1), ecs.Get<CharacterComponent>(charId).LastLoginUtc);
        }

        [Fact]
        public void RecordLogout_on_unknown_entity_is_a_noop()
        {
            var (system, _, _) = Build();

            // Must not throw
            system.RecordLogout(99999u);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // FakeClock: CreatedAtUtc and LastLoginUtc are the same instant at creation
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CreateCharacterAsync_CreatedAtUtc_equals_LastLoginUtc_at_creation()
        {
            var (system, ecs, clock) = Build();
            var t = new DateTime(2025, 5, 10, 9, 0, 0, DateTimeKind.Utc);
            clock.UtcNow = t;
            var accountId = await system.CreateAccountAsync("delta", "pw");

            var charId = await system.CreateCharacterAsync(accountId, "Fili");

            var character = ecs.Get<CharacterComponent>(charId);
            Assert.Equal(character.CreatedAtUtc, character.LastLoginUtc);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // INV-5: AccountSystem does not hold IEventBus
        // ═════════════════════════════════════════════════════════════════════════

        [Fact]
        public void AccountSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(AccountSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AccountSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus");
            }
        }
    }
}
