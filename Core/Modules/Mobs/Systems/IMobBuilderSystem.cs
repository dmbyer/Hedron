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
    }

    public readonly record struct MobCreationResult(uint MobEntityId, string BlueprintId, MobTemplate Template);
}
