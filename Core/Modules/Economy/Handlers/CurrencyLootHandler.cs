using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Mobs.Events;

namespace Hedron.Core.Modules.Economy.Handlers
{
    /// <summary>
    /// Handles <see cref="MobDiedEvent"/>: rolls the mob's currency loot and deposits each
    /// rolled amount into the killer's wallet, then publishes one <see cref="CurrencyAwardedEvent"/>
    /// per currency awarded.
    ///
    /// <para>
    /// Priority 20 (<see cref="HandlerPriority.Domain"/>) — independent of <c>SpawnSystem</c>'s
    /// slot-vacancy read on the same event; no inter-handler ordering constraint between them.
    /// Both run pre-destroy because <c>CombatMobDeathHandler</c> publishes <c>MobDiedEvent</c>
    /// before calling <c>DestroyEntity</c>.
    /// </para>
    ///
    /// <para>
    /// <c>KillerEntityId == 0</c> → discard (currency lost; no deposit, no event, no corpse entity).
    /// </para>
    ///
    /// <para>
    /// INV-8: this handler orchestrates only — the roll lives in <see cref="ICurrencyLootSystem"/>
    /// and the mutation lives in <see cref="IWalletSystem"/>. No game rule is held here.
    /// </para>
    /// </summary>
    public sealed class CurrencyLootHandler : IEventHandler<MobDiedEvent>
    {
        private readonly ICurrencyLootSystem _lootSystem;
        private readonly IWalletSystem _walletSystem;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public CurrencyLootHandler(
            ICurrencyLootSystem lootSystem,
            IWalletSystem walletSystem,
            IEventBus eventBus)
        {
            _lootSystem = lootSystem;
            _walletSystem = walletSystem;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(MobDiedEvent @event)
        {
            // No attributable killer → discard currency (INV plan: no corpse/pile entity).
            if (@event.KillerEntityId == 0)
                return;

            // Mob is still live at this point (CombatMobDeathHandler awaits PublishAsync before DestroyEntity).
            var result = _lootSystem.RollLoot(@event.MobEntityId);

            foreach (var (currency, amount) in result.Awards)
            {
                if (amount <= 0)
                    continue;

                _walletSystem.Deposit(@event.KillerEntityId, currency, amount);

                await _eventBus.PublishAsync(
                    new CurrencyAwardedEvent(@event.KillerEntityId, currency, amount))
                    .ConfigureAwait(false);
            }
        }
    }
}
