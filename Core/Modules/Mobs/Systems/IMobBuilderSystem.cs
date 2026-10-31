using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;

namespace Hedron.Core.Modules.Mobs.Systems
{
    /// <summary>
    /// Admin mob authoring. <see cref="CreateMob"/> registers a template only — the world instance
    /// comes from a room spawn rule. Every <c>Set*</c> method is <b>blueprint-keyed</b> (INV-21): it
    /// updates the registered <see cref="MobTemplate"/> and every live <b>world</b> instance of that
    /// blueprint (never a <see cref="PersistentEntity"/>), and returns the number of instances updated
    /// (0 is valid). Each returns <c>-1</c> when <paramref name="blueprintId"/> names no mob template.
    /// INV-5: none publishes events or calls persistence.
    /// </summary>
    public interface IMobBuilderSystem
    {
        /// <summary>Registers a new ad-hoc mob template with a unique blueprint id. Creates no entity.</summary>
        MobCreationResult CreateMob(string name);

        int SetMobName(string blueprintId, string name);
        int SetMobDescription(string blueprintId, string description);
        int SetMobKeywords(string blueprintId, IReadOnlyList<string> keywords);
        int SetMobType(string blueprintId, MobType mobType);

        /// <summary>
        /// Sets an attribute. Valid properties: level, hp, mind, body, spirit, attunement, maxmana,
        /// maxstamina, maxastra. Pool invariant: when hp/maxmana/maxstamina/maxastra is set, CurrentX
        /// is clamped to the new max on each instance.
        /// </summary>
        int SetAttribute(string blueprintId, string property, int value);

        /// <summary>
        /// Sets <see cref="ProtectionFlags"/>. <see cref="ProtectionFlags.None"/> removes the
        /// <see cref="ProtectionComponent"/> (mirrors the opt-in default in <see cref="MobTemplate.Apply"/>).
        /// </summary>
        int SetMobProtection(string blueprintId, ProtectionFlags flags);

        /// <summary>Sets the Ascension tier tag (<c>0</c>&#8211;<c>6</c>); callers range-validate.</summary>
        int SetMobTier(string blueprintId, int tier);

        /// <summary>Sets the descriptive Band tag (<c>0</c>&#8211;<c>3</c>); callers range-validate.</summary>
        int SetMobBand(string blueprintId, int band);

        /// <summary>Sets the per-mob combat-kill XP scale; callers validate non-negative.</summary>
        int SetMobXpScale(string blueprintId, double xpScale);

        /// <summary>
        /// Configures or removes the shop. When <paramref name="isShop"/> is <see langword="false"/>,
        /// removes any <see cref="ShopComponent"/> and clears the template's shop fields. When
        /// <see langword="true"/>, adds or updates <see cref="ShopComponent"/>; <paramref name="baseStock"/>
        /// replaces the full base-stock list (<see langword="null"/> leaves it unchanged). Does not seed
        /// the till / spawn stock (runtime-spawn concerns handled by <c>ShopkeeperSpawnHandler</c>).
        /// </summary>
        int SetMobShop(
            string blueprintId,
            bool isShop,
            CurrencyId acceptedCurrency = CurrencyId.Coin,
            long tillSeed = 0,
            decimal? ratioOverride = null,
            IReadOnlyList<ShopStockRow>? baseStock = null);
    }

    public readonly record struct MobCreationResult(string BlueprintId, MobTemplate Template);
}
