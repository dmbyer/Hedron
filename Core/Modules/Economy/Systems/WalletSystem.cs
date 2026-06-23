using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Economy.Components;

namespace Hedron.Core.Modules.Economy.Systems
{
    /// <summary>
    /// Domain system that owns all mutations to <see cref="WalletComponent"/>.
    /// INV-5: pure — returns results only; never touches the event bus or persistence.
    /// INV-1/INV-2: depends only on <see cref="EntityService"/> (ECS layer); no upward calls.
    /// </summary>
    public sealed class WalletSystem : IWalletSystem
    {
        private readonly EntityService _ecs;

        public WalletSystem(EntityService ecs)
        {
            _ecs = ecs;
        }

        /// <inheritdoc/>
        public long GetBalance(uint entityId, CurrencyId currency)
        {
            if (!_ecs.TryGet<WalletComponent>(entityId, out var wallet))
                return 0L;

            return wallet!.Balances.TryGetValue(currency, out var balance) ? balance : 0L;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<CurrencyId, long> GetBalances(uint entityId)
        {
            if (!_ecs.TryGet<WalletComponent>(entityId, out var wallet))
                return new Dictionary<CurrencyId, long>();

            // Return a snapshot copy so callers cannot observe mutations made after this call.
            return new Dictionary<CurrencyId, long>(wallet!.Balances);
        }

        /// <inheritdoc/>
        public bool Deposit(uint entityId, CurrencyId currency, long amount)
        {
            if (amount < 0)
                return false;

            var wallet = GetOrCreateWallet(entityId);
            wallet.Balances.TryGetValue(currency, out var current);
            wallet.Balances[currency] = current + amount;
            return true;
        }

        /// <inheritdoc/>
        public bool TryWithdraw(uint entityId, CurrencyId currency, long amount)
        {
            if (!_ecs.TryGet<WalletComponent>(entityId, out var wallet))
                return false;

            wallet!.Balances.TryGetValue(currency, out var current);
            if (current < amount)
                return false;

            wallet.Balances[currency] = current - amount;
            return true;
        }

        /// <inheritdoc/>
        public bool CanAfford(uint entityId, CurrencyId currency, long amount)
        {
            return GetBalance(entityId, currency) >= amount;
        }

        /// <inheritdoc/>
        public bool Transfer(uint from, uint to, CurrencyId currency, long amount)
        {
            // Self-transfer: balance-preserving no-op.
            if (from == to)
                return true;

            if (!CanAfford(from, currency, amount))
                return false;

            // Both mutations succeed because CanAfford already confirmed from can afford it.
            TryWithdraw(from, currency, amount);
            Deposit(to, currency, amount);
            return true;
        }

        /// <inheritdoc/>
        public void SetBalance(uint entityId, CurrencyId currency, long amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount),
                    $"SetBalance requires a non-negative amount; got {amount}.");

            var wallet = GetOrCreateWallet(entityId);
            wallet.Balances[currency] = amount;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the entity's <see cref="WalletComponent"/>, creating and adding it if absent
        /// (the "on first deposit" creation semantics).
        /// </summary>
        private WalletComponent GetOrCreateWallet(uint entityId)
        {
            if (_ecs.TryGet<WalletComponent>(entityId, out var existing))
                return existing!;

            var fresh = new WalletComponent();
            _ecs.AddComponent(entityId, fresh);
            return fresh;
        }
    }
}
