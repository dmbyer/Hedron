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
        /// Valid properties: level, hp, str, dex, con.
        /// INV-5: does not publish events or call persistence.
        /// INV-8: CurrentHp clamping on hp change is enforced here.
        /// </summary>
        void SetAttribute(uint mobEntityId, MobTemplate template, string property, int value);
    }

    public readonly record struct MobCreationResult(uint MobEntityId, string BlueprintId, MobTemplate Template);
}
