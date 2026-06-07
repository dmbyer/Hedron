using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Aspects
{
    public interface IAspectRegistry : IRegistry<AspectId, AspectDefinition> { }

    public sealed class AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
    {
        public AspectRegistry() : base(CreateRows(), d => d.Id) { }

        private static IEnumerable<AspectDefinition> CreateRows() => new AspectDefinition[]
        {
            new(AspectId.Fire,      "Fire",      "The searing force of flame.",              AspectCategory.Elemental),
            new(AspectId.Ice,       "Ice",       "The numbing chill of frost.",              AspectCategory.Elemental),
            new(AspectId.Lightning, "Lightning", "The crackling power of storm.",            AspectCategory.Elemental),
            new(AspectId.Void,      "Void",      "The consuming darkness of the abyss.",     AspectCategory.Arcane),
            new(AspectId.Nature,    "Nature",    "The primal force of the living world.",    AspectCategory.Primal),
            new(AspectId.Light,     "Light",     "The radiant energy of pure essence.",      AspectCategory.Arcane),
        };
    }
}
