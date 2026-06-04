using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities.Systems;

namespace Hedron.Core.Modules.Abilities
{
    /// <summary>
    /// Read-only service that maps a player's typed verb token to a known Active Skill ability id.
    /// Pure lookup — no events, no mutations. Used exclusively by
    /// <see cref="Hedron.Core.Commands.CommandDispatcher"/> Phase 3.
    /// </summary>
    public sealed class AbilityVerbResolver : IAbilityVerbResolver
    {
        private readonly IAbilitySystem _abilitySystem;
        private readonly IAbilityRegistry _abilityRegistry;

        public AbilityVerbResolver(IAbilitySystem abilitySystem, IAbilityRegistry abilityRegistry)
        {
            _abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            _abilityRegistry = abilityRegistry ?? throw new ArgumentNullException(nameof(abilityRegistry));
        }

        /// <inheritdoc />
        public bool TryResolve(uint actorEntityId, string verbToken, out string abilityId)
        {
            abilityId = string.Empty;
            var candidates = GetMatchingSkillIds(actorEntityId, verbToken);
            if (candidates.Count == 1)
            {
                abilityId = candidates[0];
                return true;
            }
            return false; // 0 = no match, 2+ = ambiguous
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetInvocableVerbs(uint actorEntityId)
            => GetActiveSkillIds(actorEntityId);

        // Returns all known ability ids that are Skill + Active.
        private IReadOnlyList<string> GetActiveSkillIds(uint actorEntityId)
        {
            var known = _abilitySystem.GetKnown(actorEntityId);
            var result = new List<string>(known.Count);
            foreach (var id in known)
            {
                if (_abilityRegistry.TryGet(id, out var def)
                    && def.Kind == AbilityKind.Skill
                    && def.Activation == Activation.Active)
                {
                    result.Add(id);
                }
            }
            return result;
        }

        // Returns all active skill ids that prefix-match verbToken.
        private IReadOnlyList<string> GetMatchingSkillIds(uint actorEntityId, string verbToken)
        {
            var all = GetActiveSkillIds(actorEntityId);
            var matches = new List<string>();
            foreach (var id in all)
            {
                if (id.StartsWith(verbToken, StringComparison.OrdinalIgnoreCase))
                    matches.Add(id);
            }
            return matches;
        }
    }
}
