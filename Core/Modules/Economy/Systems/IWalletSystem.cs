using System.Collections.Generic;

namespace Hedron.Core.Modules.Economy.Systems
{
    /// <summary>
    /// Single wallet-mutation seam for all currency flows (loot, shopping, admin grants, trade).
    /// INV-5: never publishes events or calls persistence — returns results only.
    ///
    /// <para>
    /// The wallet is entity-keyed: any entity carrying (or receiving) a
    /// <c>WalletComponent</c> is a valid holder — players, vendor tills, guild vaults, bank
    /// accounts, etc. Authorization of who may call which method is the responsibility of the
    /// caller (command or handler), not this system.
    /// </para>
    /// </summary>
    public interface IWalletSystem
    {
        /// <summary>
        /// Returns the current balance of <paramref name="currency"/> for <paramref name="entityId"/>
        /// in base units. Returns 0 if the entity has no <c>WalletComponent</c> or no entry for
        /// the currency.
        /// </summary>
        long GetBalance(uint entityId, CurrencyId currency);

        /// <summary>
        /// Returns all non-negative balances held by <paramref name="entityId"/>.
        /// Returns an empty dictionary if the entity has no <c>WalletComponent</c>.
        /// The returned dictionary is a read-only snapshot; callers must not cache it across
        /// mutations.
        /// </summary>
        IReadOnlyDictionary<CurrencyId, long> GetBalances(uint entityId);

        /// <summary>
        /// Adds <paramref name="amount"/> base units of <paramref name="currency"/> to
        /// <paramref name="entityId"/>'s wallet. Creates a <c>WalletComponent</c> on first deposit.
        /// </summary>
        /// <param name="entityId">The entity receiving the deposit.</param>
        /// <param name="currency">The currency family to deposit into.</param>
        /// <param name="amount">The amount in base units. Must be &gt;= 0.</param>
        /// <returns>
        /// <see langword="true"/> on success; <see langword="false"/> when
        /// <paramref name="amount"/> is negative (no mutation).
        /// </returns>
        bool Deposit(uint entityId, CurrencyId currency, long amount);

        /// <summary>
        /// Attempts to withdraw <paramref name="amount"/> base units of <paramref name="currency"/>
        /// from <paramref name="entityId"/>'s wallet.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> and decrements by exactly <paramref name="amount"/> when the
        /// entity can afford it; <see langword="false"/> and leaves the balance unchanged when
        /// insufficient funds.
        /// </returns>
        bool TryWithdraw(uint entityId, CurrencyId currency, long amount);

        /// <summary>
        /// Returns whether <paramref name="entityId"/> has at least <paramref name="amount"/>
        /// base units of <paramref name="currency"/>. Mutates nothing.
        /// </summary>
        bool CanAfford(uint entityId, CurrencyId currency, long amount);

        /// <summary>
        /// Atomically transfers <paramref name="amount"/> base units of <paramref name="currency"/>
        /// from <paramref name="from"/> to <paramref name="to"/>, provided <paramref name="from"/>
        /// can afford it. Neither wallet is mutated on insufficient funds (no partial transfer).
        /// Self-transfer (<paramref name="from"/> == <paramref name="to"/>) is a balance-preserving
        /// no-op returning <see langword="true"/>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> on success (or self-transfer); <see langword="false"/> when
        /// <paramref name="from"/> cannot afford the transfer.
        /// </returns>
        bool Transfer(uint from, uint to, CurrencyId currency, long amount);

        /// <summary>
        /// Absolute-sets the balance of <paramref name="currency"/> for <paramref name="entityId"/>
        /// to <paramref name="amount"/> base units. Creates a <c>WalletComponent</c> if absent.
        /// </summary>
        /// <param name="amount">Target balance in base units. Must be &gt;= 0.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when <paramref name="amount"/> is negative.
        /// </exception>
        void SetBalance(uint entityId, CurrencyId currency, long amount);
    }
}
