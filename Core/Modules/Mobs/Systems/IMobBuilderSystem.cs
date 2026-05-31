using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs.Templates;

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
    }

    public readonly record struct MobCreationResult(uint MobEntityId, string BlueprintId, MobTemplate Template);
}
