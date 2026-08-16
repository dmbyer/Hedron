using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;

namespace Hedron.Core.Modules.Mobs.Systems
{
    public interface IMobBuilderSystem
    {
        MobCreationResult CreateMob(string name, uint roomEntityId);
        void SetMobName(uint mobEntityId, string name);
        void SetMobDescription(uint mobEntityId, string description);
        void SetMobKeywords(uint mobEntityId, IReadOnlyList<string> keywords);
        void SetMobType(uint mobEntityId, MobType mobType);
        /// <summary>
        /// Mutates an attribute on the live entity and the in-memory template.
        /// Valid properties: level, hp, mind, body, spirit, attunement, maxmana, maxstamina, maxastra.
        /// INV-5: does not publish events or call persistence.
        /// Pool invariant: when hp/maxmana/maxstamina/maxastra is set, CurrentX is clamped to the new max.
        /// </summary>
        void SetAttribute(uint mobEntityId, MobTemplate template, string property, int value);
        /// <summary>
        /// Dual-writes <see cref="ProtectionFlags"/> onto the live <see cref="ProtectionComponent"/>
        /// and <see cref="MobTemplate.Protection"/>. When <paramref name="flags"/> is
        /// <see cref="ProtectionFlags.None"/>, removes any existing <see cref="ProtectionComponent"/>
        /// from the live entity (mirrors the opt-in default in <see cref="MobTemplate.Apply"/>).
        /// INV-5: does not publish events or call persistence.
        /// </summary>
        void SetMobProtection(uint mobEntityId, ProtectionFlags flags);

        /// <summary>
        /// Dual-writes the Ascension tier tag (<c>0</c>&#8211;<c>6</c>) onto the live
        /// <see cref="MobDataComponent.Tier"/> and <see cref="MobTemplate.Tier"/>.
        /// Callers (<c>SetMobCommand</c>) range-validate before calling.
        /// INV-5: does not publish events or call persistence.
        /// </summary>
        void SetMobTier(uint mobEntityId, int tier);

        /// <summary>
        /// Dual-writes the descriptive Band tag (<c>0</c>&#8211;<c>3</c>) onto the live
        /// <see cref="MobDataComponent.Band"/> and <see cref="MobTemplate.Band"/>.
        /// Callers (<c>SetMobCommand</c>) range-validate before calling.
        /// INV-5: does not publish events or call persistence.
        /// </summary>
        void SetMobBand(uint mobEntityId, int band);

        /// <summary>
        /// Dual-writes the per-mob combat-kill XP scale onto the live
        /// <see cref="MobDataComponent.XpScale"/> and <see cref="MobTemplate.XpScale"/>.
        /// Callers (<c>SetMobCommand</c>) range-validate (non-negative) before calling.
        /// INV-5: does not publish events or call persistence.
        /// </summary>
        void SetMobXpScale(uint mobEntityId, double xpScale);

        /// <summary>
        /// Configures or removes the shop on a mob entity and its in-memory template.
        /// When <paramref name="isShop"/> is <see langword="false"/>, removes any existing
        /// <see cref="ShopComponent"/> from the live entity and clears the template's shop
        /// fields (opt-in default: most mobs are not shopkeepers).
        /// When <see langword="true"/>, adds or updates <see cref="ShopComponent"/> on both
        /// the live entity and the template; <paramref name="baseStock"/> replaces the full
        /// base-stock list (pass <see langword="null"/> to leave base stock unchanged when
        /// toggling other fields).
        /// INV-5: does not publish events, call persistence, or seed the till / spawn stock
        /// (those are runtime-spawn concerns handled by <c>ShopkeeperSpawnHandler</c>).
        /// </summary>
        void SetMobShop(
            uint mobEntityId,
            bool isShop,
            CurrencyId acceptedCurrency = CurrencyId.Coin,
            long tillSeed = 0,
            decimal? ratioOverride = null,
            IReadOnlyList<ShopStockRow>? baseStock = null);
    }

    public readonly record struct MobCreationResult(uint MobEntityId, string BlueprintId, MobTemplate Template);
}
