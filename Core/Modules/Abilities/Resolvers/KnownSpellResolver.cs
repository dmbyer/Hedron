using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities.Systems;

namespace Hedron.Core.Modules.Abilities.Resolvers
{
    /// <summary>
    /// Resolves a spell name/id token against the invoker's known Active Spells.
    /// Emits two <see cref="ResolvedCandidate"/> entries per known spell (one for the id,
    /// one for the display name), both sharing the same canonical value (the ability id).
    /// Used by <see cref="Commands.CastCommand"/> to prefix-match the spell argument.
    /// </summary>
    public sealed class KnownSpellResolver : IArgumentResolver
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public KnownSpellResolver(IAbilitySystem abilitySystem, IAbilityRegistry abilityRegistry)
        {
            _abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
        }

        public IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context)
        {
            var known = _abilitySystem.GetKnown(context.InvokerEntityId);
            var candidates = new List<ResolvedCandidate>(known.Count * 2);

            foreach (var id in known)
            {
                if (!_abilityRegistry.TryGet(id, out var def)) continue;
                if (def.Kind != AbilityKind.Spell || def.Activation != Activation.Active) continue;

                // Match by ability id (e.g. "empower", "blood_pact") and by display name (e.g. "Empower").
                candidates.Add(new ResolvedCandidate(id, id));
                candidates.Add(new ResolvedCandidate(def.Name, id));
            }

            return candidates;
        }
    }
}
