using System.Collections.Generic;
using System.Reflection;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="WalletSystem"/>.
    ///
    /// Coverage contract: Wallet-ledger, Withdraw-atomicity, Transfer-atomicity,
    /// CanAfford, and Admin-set postconditions from docs/implementation-plans/currency-foundation.md.
    /// </summary>
    public sealed class WalletSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (WalletSystem system, EntityService ecs) Build()
        {
            var ecs = new EntityService();
            var system = new WalletSystem(ecs);
            return (system, ecs);
        }

        // ── Deposit ───────────────────────────────────────────────────────────────

        [Fact]
        public void Deposit_increases_balance_by_exact_amount()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            sys.Deposit(entity, CurrencyId.Coin, 50L);

            Assert.Equal(50L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void Deposit_accumulates_on_successive_calls()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            sys.Deposit(entity, CurrencyId.Coin, 30L);
            sys.Deposit(entity, CurrencyId.Coin, 20L);

            Assert.Equal(50L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void Deposit_creates_WalletComponent_on_first_deposit()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            Assert.False(ecs.HasComponent<WalletComponent>(entity),
                "Precondition: WalletComponent must be absent before first deposit.");

            sys.Deposit(entity, CurrencyId.Coin, 1L);

            Assert.True(ecs.HasComponent<WalletComponent>(entity),
                "WalletComponent must be created on first deposit.");
        }

        [Fact]
        public void Deposit_negative_amount_returns_false_and_makes_no_mutation()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            var result = sys.Deposit(entity, CurrencyId.Coin, -1L);

            Assert.False(result, "Deposit of a negative amount must return false.");
            Assert.False(ecs.HasComponent<WalletComponent>(entity),
                "No WalletComponent must be created when deposit is rejected.");
            Assert.Equal(0L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void Deposit_zero_is_accepted_and_creates_wallet()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            var result = sys.Deposit(entity, CurrencyId.Coin, 0L);

            Assert.True(result, "Deposit of zero must be accepted.");
            Assert.Equal(0L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        // ── TryWithdraw ───────────────────────────────────────────────────────────

        [Fact]
        public void TryWithdraw_returns_true_and_decrements_when_affordable()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 100L);

            var result = sys.TryWithdraw(entity, CurrencyId.Coin, 40L);

            Assert.True(result, "TryWithdraw must return true when affordable.");
            Assert.Equal(60L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void TryWithdraw_returns_false_and_leaves_balance_unchanged_when_insufficient()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 10L);

            var result = sys.TryWithdraw(entity, CurrencyId.Coin, 50L);

            Assert.False(result, "TryWithdraw must return false on insufficient funds.");
            Assert.Equal(10L, sys.GetBalance(entity, CurrencyId.Coin)); // Balance must be unchanged after failed withdrawal.
        }

        [Fact]
        public void TryWithdraw_returns_false_when_entity_has_no_wallet()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            var result = sys.TryWithdraw(entity, CurrencyId.Coin, 1L);

            Assert.False(result, "TryWithdraw must return false when entity has no wallet.");
        }

        [Fact]
        public void TryWithdraw_exact_balance_succeeds_and_leaves_zero()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 25L);

            var result = sys.TryWithdraw(entity, CurrencyId.Coin, 25L);

            Assert.True(result);
            Assert.Equal(0L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        // ── CanAfford ────────────────────────────────────────────────────────────

        [Fact]
        public void CanAfford_returns_true_when_balance_equals_amount()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 50L);

            Assert.True(sys.CanAfford(entity, CurrencyId.Coin, 50L));
        }

        [Fact]
        public void CanAfford_returns_true_when_balance_exceeds_amount()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 100L);

            Assert.True(sys.CanAfford(entity, CurrencyId.Coin, 50L));
        }

        [Fact]
        public void CanAfford_returns_false_when_balance_is_less_than_amount()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 10L);

            Assert.False(sys.CanAfford(entity, CurrencyId.Coin, 50L));
        }

        [Fact]
        public void CanAfford_does_not_mutate_balance()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 75L);

            _ = sys.CanAfford(entity, CurrencyId.Coin, 75L);

            Assert.Equal(75L, sys.GetBalance(entity, CurrencyId.Coin)); // CanAfford must not mutate the balance.
        }

        [Fact]
        public void CanAfford_returns_false_when_entity_has_no_wallet()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            Assert.False(sys.CanAfford(entity, CurrencyId.Coin, 1L));
        }

        // ── Transfer ─────────────────────────────────────────────────────────────

        [Fact]
        public void Transfer_debits_from_and_credits_to_when_affordable()
        {
            var (sys, ecs) = Build();
            var sender = ecs.CreateEntity().Id;
            var receiver = ecs.CreateEntity().Id;
            sys.Deposit(sender, CurrencyId.Coin, 100L);

            var result = sys.Transfer(sender, receiver, CurrencyId.Coin, 40L);

            Assert.True(result, "Transfer must return true when affordable.");
            Assert.Equal(60L, sys.GetBalance(sender, CurrencyId.Coin));
            Assert.Equal(40L, sys.GetBalance(receiver, CurrencyId.Coin));
        }

        [Fact]
        public void Transfer_returns_false_and_neither_wallet_mutated_on_insufficient_funds()
        {
            var (sys, ecs) = Build();
            var sender = ecs.CreateEntity().Id;
            var receiver = ecs.CreateEntity().Id;
            sys.Deposit(sender, CurrencyId.Coin, 10L);
            sys.Deposit(receiver, CurrencyId.Coin, 5L);

            var result = sys.Transfer(sender, receiver, CurrencyId.Coin, 50L);

            Assert.False(result, "Transfer must return false when sender cannot afford it.");
            Assert.Equal(10L, sys.GetBalance(sender, CurrencyId.Coin)); // Sender's balance must be unchanged on failed transfer.
            Assert.Equal(5L, sys.GetBalance(receiver, CurrencyId.Coin)); // Receiver's balance must be unchanged on failed transfer.
        }

        [Fact]
        public void Transfer_self_transfer_is_balance_preserving_no_op_returning_true()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 100L);

            var result = sys.Transfer(entity, entity, CurrencyId.Coin, 50L);

            Assert.True(result, "Self-transfer must return true.");
            Assert.Equal(100L, sys.GetBalance(entity, CurrencyId.Coin)); // Self-transfer must not change the balance.
        }

        [Fact]
        public void Transfer_self_transfer_with_no_wallet_returns_true()
        {
            // Self-transfer is always a no-op — even without a wallet.
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            var result = sys.Transfer(entity, entity, CurrencyId.Coin, 0L);

            Assert.True(result);
        }

        // ── SetBalance ───────────────────────────────────────────────────────────

        [Fact]
        public void SetBalance_absolute_sets_the_balance()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 50L);

            sys.SetBalance(entity, CurrencyId.Coin, 200L);

            Assert.Equal(200L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void SetBalance_creates_wallet_if_absent()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            sys.SetBalance(entity, CurrencyId.Coin, 99L);

            Assert.True(ecs.HasComponent<WalletComponent>(entity));
            Assert.Equal(99L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        [Fact]
        public void SetBalance_throws_on_negative_amount()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => sys.SetBalance(entity, CurrencyId.Coin, -1L));
        }

        [Fact]
        public void SetBalance_to_zero_sets_balance_to_zero()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 100L);

            sys.SetBalance(entity, CurrencyId.Coin, 0L);

            Assert.Equal(0L, sys.GetBalance(entity, CurrencyId.Coin));
        }

        // ── GetBalances ───────────────────────────────────────────────────────────

        [Fact]
        public void GetBalances_returns_empty_when_no_wallet()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;

            var balances = sys.GetBalances(entity);

            Assert.Empty(balances);
        }

        [Fact]
        public void GetBalances_returns_snapshot_of_all_entries()
        {
            var (sys, ecs) = Build();
            var entity = ecs.CreateEntity().Id;
            sys.Deposit(entity, CurrencyId.Coin, 123L);

            var balances = sys.GetBalances(entity);

            Assert.True(balances.ContainsKey(CurrencyId.Coin));
            Assert.Equal(123L, balances[CurrencyId.Coin]);
        }

        // ── INV-5: WalletSystem must not hold IEventBus ───────────────────────────

        [Fact]
        public void WalletSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(WalletSystem).GetFields(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: WalletSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus.");
            }
        }
    }
}
