using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Regeneration.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// One isolated run's system graph — a plain facade over a fresh <see cref="EntityService"/>
    /// plus a hand-built composition mirroring the <c>Hedron.Tests</c> harness (never the host's
    /// live world; INV-12 nuance). Produced by <see cref="ISandboxWorldFactory.Create"/>; the
    /// executor drives it directly, calling system methods with no event bus involved (INV-5).
    /// </summary>
    public sealed class SandboxWorld
    {
        public EntityService EntityService { get; }
        public IRandom Random { get; }
        public IEffectSystem Effects { get; }
        public IAttributeSystem Attributes { get; }
        public IStatSystem Stats { get; }
        public IAspectSystem Aspects { get; }
        public ICombatSystem Combat { get; }
        public IAbilitySystem Abilities { get; }
        public IEntityStateService EntityState { get; }
        public IRegenerationSystem Regeneration { get; }
        public IProgressionSystem Progression { get; }
        public IAscensionSystem Ascension { get; }

        /// <summary>The single shared room entity every sandbox combatant is placed in.</summary>
        public uint ArenaRoomEntityId { get; }

        public SandboxWorld(
            EntityService entityService,
            IRandom random,
            IEffectSystem effects,
            IAttributeSystem attributes,
            IStatSystem stats,
            IAspectSystem aspects,
            ICombatSystem combat,
            IAbilitySystem abilities,
            IEntityStateService entityState,
            IRegenerationSystem regeneration,
            IProgressionSystem progression,
            IAscensionSystem ascension,
            uint arenaRoomEntityId)
        {
            EntityService = entityService;
            Random = random;
            Effects = effects;
            Attributes = attributes;
            Stats = stats;
            Aspects = aspects;
            Combat = combat;
            Abilities = abilities;
            EntityState = entityState;
            Regeneration = regeneration;
            Progression = progression;
            Ascension = ascension;
            ArenaRoomEntityId = arenaRoomEntityId;
        }
    }
}
