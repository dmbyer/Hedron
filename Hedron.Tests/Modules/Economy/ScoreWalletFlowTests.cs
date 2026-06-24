using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Commands;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 3 — flow tests for the <c>score</c> command carrying wallet balances (Flow B).
    ///
    /// Coverage contract: Score-display postcondition from
    /// docs/implementation-plans/currency-foundation.md (WP-3, Tier 3):
    ///
    ///   A player with a non-empty wallet runs <c>score</c>; assert
    ///   <see cref="ScoreDisplayMessage"/> carries the raw balance pairs
    ///   (message <b>structure/type</b>, not exact prose).
    /// </summary>
    public sealed class ScoreWalletFlowTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────────

        /// <summary>Minimal IStatSystem stub that returns 0 for all scores.</summary>
        private sealed class ZeroStatSystem : IStatSystem
        {
            public int GetEffectiveMind(uint entityId) => 0;
            public int GetEffectiveBody(uint entityId) => 0;
            public int GetEffectiveSpirit(uint entityId) => 0;
            public int GetEffectiveAttunement(uint entityId) => 0;
            public int GetEffectiveAttackPower(uint entityId) => 0;
            public int GetEffectiveDefense(uint entityId) => 0;
            public int GetCurrentHp(uint entityId) => 0;
            public int GetMaxHp(uint entityId) => 0;
            public int Get(uint entityId, ScoreId score) => 0;
        }

        /// <summary>Minimal IEntityStateService stub that always returns false for state checks.</summary>
        private sealed class NoStateService : IEntityStateService
        {
            public bool TryEnterState(uint entityId, EntityStateFlags state, out string? failReason)
            { failReason = null; return true; }
            public void ExitState(uint entityId, EntityStateFlags state) { }
            public bool IsInState(uint entityId, EntityStateFlags state) => false;
            public EntityStateFlags GetStates(uint entityId) => EntityStateFlags.None;
        }

        // ── ParsedArguments factory (internal ctor via reflection) ─────────────────

        private static ParsedArguments EmptyArgs()
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;
            return (ParsedArguments)ctor.Invoke(new object[] { new Dictionary<string, object?>() });
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Score_command_ScoreDisplayMessage_carries_wallet_balances_when_non_empty()
        {
            // Arrange
            var ecs = new EntityService();
            var walletSystem = new WalletSystem(ecs);

            var playerId = ecs.CreateEntity().Id;
            ecs.AddComponent(playerId, new CharacterComponent { CharacterName = "Tester" });
            ecs.AddComponent(playerId, new AttributesComponent { Level = 1 });
            ecs.AddComponent(playerId, new PoolsComponent { CurrentHp = 50, MaxHp = 100 });

            // Give the player some coin so the wallet is non-empty
            walletSystem.Deposit(playerId, CurrencyId.Coin, 105L);

            var command = new ScoreCommand(ecs, new ZeroStatSystem(), new NoStateService(), walletSystem);

            var output = new RecordingOutput();
            var session = new StubSession(playerId);
            var context = new CommandContext(
                session,
                playerId,
                EmptyArgs(),
                output.WriterFor(playerId),
                Services: null!);

            // Act
            await command.ExecuteAsync(context);

            // Assert — ScoreDisplayMessage must be written and carry the raw balance pairs.
            output.AssertMessage<ScoreDisplayMessage>(playerId);

            var scoreMsg = (ScoreDisplayMessage)output.All
                .First(r => r.MessageType == typeof(ScoreDisplayMessage)).Message;

            Assert.NotNull(scoreMsg.WalletBalances);
            Assert.True(scoreMsg.WalletBalances!.ContainsKey(CurrencyId.Coin),
                "ScoreDisplayMessage.WalletBalances must contain the Coin currency.");
            Assert.Equal(105L, scoreMsg.WalletBalances[CurrencyId.Coin]);
        }

        [Fact]
        public async Task Score_command_ScoreDisplayMessage_has_empty_wallet_when_no_WalletComponent()
        {
            // Arrange
            var ecs = new EntityService();
            var walletSystem = new WalletSystem(ecs);

            var playerId = ecs.CreateEntity().Id;
            ecs.AddComponent(playerId, new CharacterComponent { CharacterName = "Poor Player" });
            ecs.AddComponent(playerId, new AttributesComponent { Level = 1 });
            ecs.AddComponent(playerId, new PoolsComponent { CurrentHp = 50, MaxHp = 100 });
            // No wallet deposit → no WalletComponent

            var command = new ScoreCommand(ecs, new ZeroStatSystem(), new NoStateService(), walletSystem);

            var output = new RecordingOutput();
            var session = new StubSession(playerId);
            var context = new CommandContext(
                session,
                playerId,
                EmptyArgs(),
                output.WriterFor(playerId),
                Services: null!);

            // Act
            await command.ExecuteAsync(context);

            // Assert — ScoreDisplayMessage is still written but wallet is empty
            output.AssertMessage<ScoreDisplayMessage>(playerId);

            var scoreMsg = (ScoreDisplayMessage)output.All
                .First(r => r.MessageType == typeof(ScoreDisplayMessage)).Message;

            Assert.NotNull(scoreMsg.WalletBalances);
            Assert.Empty(scoreMsg.WalletBalances!);
        }
    }
}
