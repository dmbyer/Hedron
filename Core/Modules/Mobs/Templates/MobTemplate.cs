using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Mobs.Templates
{
    public sealed class MobTemplate : IEntityTemplate
    {
        public string BlueprintId { get; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public MobType MobType { get; set; } = MobType.None;

        /// <summary>Blueprint id of the room this mob spawns in. Empty means no spawn location.</summary>
        public string SpawnRoomBlueprintId { get; set; } = string.Empty;

        public int Level { get; set; } = 0;
        public int MaxHp { get; set; } = 0;
        public int Mind { get; set; } = 0;
        public int Body { get; set; } = 0;
        public int Spirit { get; set; } = 0;
        public int Attunement { get; set; } = 0;
        public int MaxMana { get; set; } = 0;
        public int MaxStamina { get; set; } = 0;
        public int MaxAstra { get; set; } = 0;

        /// <summary>
        /// Optional per-currency loot range (min, max in base units / copper).
        /// When a currency key is absent or both min and max are zero, no loot component
        /// entry is written for that currency (opt-in default: no drop).
        /// Authored via YAML / Blazor editor and applied by <see cref="Apply"/>.
        /// </summary>
        public Dictionary<CurrencyId, (int Min, int Max)> CurrencyLoot { get; set; } = new();

        /// <summary>
        /// Optional protection flags. When <see cref="ProtectionFlags.None"/> (the default),
        /// no <see cref="ProtectionComponent"/> is added in <see cref="Apply"/> (opt-in default,
        /// mirrors the <c>CurrencyLoot</c> precedent). Durable form is YAML; NOT <c>[Persistent]</c>.
        /// </summary>
        public ProtectionFlags Protection { get; set; } = ProtectionFlags.None;

        public MobTemplate(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public void Apply(Entity entity, EntityService entityService)
        {
            entityService.AddComponent(entity.Id, new MobDataComponent
            {
                Name = Name,
                Description = Description,
                Keywords = new List<string>(Keywords),
                MobType = MobType,
            });

            var level = Level > 0 ? Level : 1;
            var maxHp = MaxHp > 0 ? MaxHp : 100;
            entityService.AddComponent(entity.Id, new AttributesComponent
            {
                Level = level,
                Mind = Mind > 0 ? Mind : 10,
                Body = Body > 0 ? Body : 10,
                Spirit = Spirit > 0 ? Spirit : 10,
                Attunement = Attunement > 0 ? Attunement : 10,
            });
            var maxMana = MaxMana > 0 ? MaxMana : 50;
            var maxStamina = MaxStamina > 0 ? MaxStamina : 50;
            var maxAstra = MaxAstra > 0 ? MaxAstra : 10;
            entityService.AddComponent(entity.Id, new PoolsComponent
            {
                MaxHp = maxHp,
                CurrentHp = maxHp,
                MaxMana = maxMana,
                CurrentMana = maxMana,
                MaxStamina = maxStamina,
                CurrentStamina = maxStamina,
                MaxAstra = maxAstra,
                CurrentAstra = maxAstra,
            });

            // Add CurrencyLootComponent only when at least one non-zero range is configured.
            // Zero / absent range → no component → no drop (opt-in default, INV-23 world content).
            var lootComp = new CurrencyLootComponent();
            foreach (var (currency, range) in CurrencyLoot)
            {
                if (range.Max > 0)
                    lootComp.Ranges[currency] = range;
            }
            if (lootComp.Ranges.Count > 0)
                entityService.AddComponent(entity.Id, lootComp);

            // Add ProtectionComponent only when flags are non-None (opt-in default, mirrors CurrencyLoot).
            // ProtectionFlags.None → no component → no protection (world-content default).
            if (Protection != ProtectionFlags.None)
                entityService.AddComponent(entity.Id, new ProtectionComponent { Flags = Protection });
        }
    }
}
