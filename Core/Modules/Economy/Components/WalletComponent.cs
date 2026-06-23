using System.Collections.Generic;
using Hedron.Core.ECS;

namespace Hedron.Core.Modules.Economy.Components
{
    /// <summary>
    /// Holds an entity's currency balances as a dictionary of base-unit <see langword="long"/> values,
    /// keyed by <see cref="CurrencyId"/>. Balances are always non-negative. A missing key means the
    /// entity holds zero of that currency.
    ///
    /// <para>
    /// Placed at any holder entity (player, vendor till, bank vault, etc.) on first deposit via
    /// <c>IWalletSystem.Deposit</c>. Entities without this component hold no currency.
    /// </para>
    ///
    /// <para>
    /// Tagged <c>[Persistent]</c> so player wallet balances survive server restarts. Dictionary keys
    /// are serialized <b>by enum name</b> (not ordinal) because <see cref="ComponentSerializer"/>
    /// uses <c>JsonStringEnumConverter</c> globally — a future <see cref="CurrencyId"/> reordering
    /// will not corrupt saved wallets.
    /// </para>
    /// </summary>
    [Persistent]
    public sealed class WalletComponent : IComponent
    {
        /// <summary>
        /// Currency balances in base units, keyed by <see cref="CurrencyId"/>.
        /// Absent keys represent a zero balance. Values are always &gt;= 0.
        /// </summary>
        public Dictionary<CurrencyId, long> Balances { get; set; } = new();
    }
}
