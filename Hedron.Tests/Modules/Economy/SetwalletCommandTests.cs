using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Commands;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 2 — handler/orchestration tests for <see cref="SetwalletCommand"/>.
    ///
    /// Coverage contract: Admin-set postcondition (orchestration half) from
    /// docs/implementation-plans/currency-foundation.md (WP-3, Tier 2):
    ///
    ///   • Absolute-set via <see cref="IWalletSystem"/>.
    ///   • Exactly one <see cref="IPersistenceSystem.SaveEntityAsync(uint, CancellationToken)"/> call.
    ///   • Exactly one <see cref="WalletSetByAdminEvent"/> published.
    ///   • Non-privileged invoker rejected (RequiredPrivileges asserted structurally).
    ///
    /// Mirrors the test pattern of <see cref="CurrencyLootHandlerTests"/> — no mocking framework.
    /// </summary>
    public sealed class SetwalletCommandTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────────

        private sealed class FakeSessionManager : ISessionManager
        {
            private readonly List<ISession> _sessions = new();

            public void Register(ISession session) => _sessions.Add(session);
            public void Unregister(uint playerEntityId) =>
                _sessions.RemoveAll(s => s.PlayerEntityId == playerEntityId);
            public ISession? GetSession(uint playerEntityId) =>
                _sessions.FirstOrDefault(s => s.PlayerEntityId == playerEntityId);
            public IReadOnlyCollection<ISession> GetAll() => _sessions.AsReadOnly();
        }

        private sealed class RecordingPersistence : IPersistenceSystem
        {
            public List<uint> SavedEntityIds { get; } = new();

            public Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
            {
                SavedEntityIds.Add(entityId);
                return Task.CompletedTask;
            }

            public Task FlushAllAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task FlushDirtyAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<uint>>(Array.Empty<uint>());
        }

        // ── World ─────────────────────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public WalletSystem Wallets { get; }
            public FakeSessionManager Sessions { get; }
            public RecordingPersistence Persistence { get; }
            public RecordingEventBus Bus { get; }
            public SetwalletCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Wallets = new WalletSystem(Ecs);
                Sessions = new FakeSessionManager();
                Persistence = new RecordingPersistence();
                Bus = new RecordingEventBus(dispatch: false);
                Command = new SetwalletCommand(Wallets, Sessions, Ecs, Bus, Persistence);
            }

            /// <summary>Creates a player with a CharacterComponent and registers a stub session.</summary>
            public uint CreatePlayer(string characterName)
            {
                var entityId = Ecs.CreateEntity().Id;
                Ecs.AddComponent(entityId, new CharacterComponent { CharacterName = characterName });
                Sessions.Register(new StubSession(entityId));
                return entityId;
            }
        }

        // ── ParsedArguments factory (internal ctor via reflection) ─────────────────

        private static ParsedArguments MakeArgs(string characterName, string currency, string amount)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;

            var values = new Dictionary<string, object?>
            {
                ["characterName"] = characterName,
                ["currency"] = currency,
                ["amount"] = amount,
            };
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(uint invokerEntityId, ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(invokerEntityId);
            return new CommandContext(
                session,
                invokerEntityId,
                args,
                output.WriterFor(invokerEntityId),
                Services: null!);
        }

        // ── RequiredPrivileges structural assertion ────────────────────────────────

        [Fact]
        public void SetwalletCommand_RequiredPrivileges_contains_AdminRequirement()
        {
            var cmd = new SetwalletCommand(null!, null!, null!, null!, null!);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
        }

        [Fact]
        public void SetwalletCommand_Category_is_Admin()
        {
            var cmd = new SetwalletCommand(null!, null!, null!, null!, null!);
            Assert.Equal(CommandCategory.Admin, cmd.Category);
        }

        [Fact]
        public void SetwalletCommand_MatchingMode_is_Full()
        {
            var cmd = new SetwalletCommand(null!, null!, null!, null!, null!);
            Assert.Equal(CommandMatchingMode.Full, cmd.MatchingMode);
        }

        // ── Absolute-set and boundary-save ────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_absolute_sets_wallet_balance_via_IWalletSystem()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Alice");
            world.Wallets.Deposit(playerId, CurrencyId.Coin, 50L); // pre-existing balance

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Alice", "Coin", "200"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(200L, world.Wallets.GetBalance(playerId, CurrencyId.Coin));
        }

        [Fact]
        public async Task ExecuteAsync_calls_SaveEntityAsync_exactly_once_on_target()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Bob");

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Bob", "Coin", "100"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Single(world.Persistence.SavedEntityIds); // exactly one save (INV-22)
            Assert.Equal(playerId, world.Persistence.SavedEntityIds[0]);
        }

        [Fact]
        public async Task ExecuteAsync_publishes_exactly_one_WalletSetByAdminEvent()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Carol");
            const uint adminId = 99u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs("Carol", "Coin", "500"), output);

            await world.Command.ExecuteAsync(ctx);

            var events = world.Bus.Published.OfType<WalletSetByAdminEvent>().ToList();
            Assert.Single(events);
            Assert.Equal(adminId, events[0].AdminEntityId);
            Assert.Equal(playerId, events[0].TargetEntityId);
            Assert.Equal(CurrencyId.Coin, events[0].Currency);
            Assert.Equal(500L, events[0].Amount);
        }

        // ── Player not found ──────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_no_mutation_when_player_not_found()
        {
            var world = new TestWorld();
            // no player named "Nobody" registered

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Nobody", "Coin", "100"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.Persistence.SavedEntityIds); // no save when player not found
            Assert.Empty(world.Bus.Published.OfType<WalletSetByAdminEvent>());
        }

        // ── Invalid currency ──────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_no_mutation_when_currency_unknown()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Dave");

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Dave", "Astral", "100"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(0L, world.Wallets.GetBalance(playerId, CurrencyId.Coin));
            Assert.Empty(world.Persistence.SavedEntityIds);
            Assert.Empty(world.Bus.Published.OfType<WalletSetByAdminEvent>());
        }

        // ── Invalid amount ────────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_no_mutation_when_amount_is_negative()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Eve");

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Eve", "Coin", "-50"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(0L, world.Wallets.GetBalance(playerId, CurrencyId.Coin));
            Assert.Empty(world.Persistence.SavedEntityIds);
        }

        // ── SetBalance to zero is valid ──────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_accepts_zero_amount_and_saves()
        {
            var world = new TestWorld();
            var playerId = world.CreatePlayer("Frank");
            world.Wallets.Deposit(playerId, CurrencyId.Coin, 200L);

            var output = new RecordingOutput();
            var ctx = MakeContext(99u, MakeArgs("Frank", "Coin", "0"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Equal(0L, world.Wallets.GetBalance(playerId, CurrencyId.Coin));
            Assert.Single(world.Persistence.SavedEntityIds);
        }
    }
}
