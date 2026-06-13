# Abilities

> Skills and spells — a unified ability kit that players learn, invoke by bare verb or `cast`, and pay for in stamina, mana, HP, or a mix. **Status:** live (slices 11-a + 11-b; `kick` / `cast empower` / passive `toughness` wired).

## What it is

An **ability** is something a player (or mob) knows and can invoke — a physical technique (`kick`, Skill) or an arcane formula (`empower`, Spell). Skills are invoked as bare verbs (`kick`, `ki`); spells go through `cast <name>`. Both share the same underlying model: a definition record carrying a Kind, an Activation mode (Active / Passive / Triggered), one or more resource Costs, a Targeting mode, and a list of Effects that execute on activation.

From a player's seat:

- Type `skills` or `spells` to see what you know and what it costs.
- Type a skill id (or any prefix) to invoke it: `kick goblin`, `ki go`.
- Type `cast empower` (or `c emp`) to cast a spell.
- Passive abilities like `toughness` are always on — no activation needed; their stat bonuses derive from the known-abilities list every time the stat pipeline reads them.
- New characters receive a configured starter set (e.g. `kick` + `empower`) at creation; an admin can grant others with `teach`.

## How it works

The feature composes three pieces:

- **`AbilitySystem`** (domain) owns the activation pipeline — validates entity state, cooldowns, and all costs atomically; spends every cost through `IAttributeSystem`; calls `IEffectSystem.Apply` for each effect; returns a structured result. It also tracks known abilities and per-ability cooldowns via `AbilitiesComponent`. The system never publishes events (INV-5).
- **`AbilityInvocationPipeline`** (initiator-tier helper) handles the player path: state-aware target resolution (`InCombat` → current opponent; explicit token → room resolver; offensive + no target → prompt), combat entry when an offensive ability opens on a new target, and the `ICombatSystem.ResolveAbilityStrike` call that routes offensive damage through defense math. Both `CastCommand` and `SkillInvocationCommand` delegate to it.
- **`AbilityEffectContributor`** (INV-24 seam) derives `WhileKnown` passive effects on read — folded into `IEffectSystem.GetModifiers` so the stat pipeline (and `score`, combat) see passive bonuses with no call-site changes.

Cooldowns are tracked in `AbilitiesComponent.CooldownRemaining` (seconds, transient) and decremented each heartbeat tick by `AbilityCooldownTickHandler`. The full activation and invocation model is the [ability-system design doc](ability-system.md).

## Systems

| System | Role |
|---|---|
| [`ability-system.md`](ability-system.md) | Activation pipeline, cost/cooldown, `IEffectContributor` passive seam, invocation routing |

## Surfaces

- **Commands** — `cast` / `c` (spell invocation), `skills` / `spells` / `abilities` (inspect), bare skill verbs (dispatcher Phase 3 fallback). Admin: `teach`, `useability`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `AbilityActivatedEvent`, `AbilityLearnedEvent`, `AbilityTaughtByAdminEvent`, `AbilityStrikeResolvedEvent`. See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Component** — `AbilitiesComponent` (`[Persistent]`, `Known` durable / `CooldownRemaining` transient). See [`../../reference/components.md`](../../reference/components.md).

## Flows

- [Abilities journey (activation · bare-verb skill invocation · offensive-opens-combat)](../../architecture/flows/flow-24-ability-activation.md) — how a player activates an ability, how a bare skill verb routes through the dispatcher, and how an offensive ability initiates combat.

## Related

- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine B (Ability); the upstream design this realizes.
- [`../../roadmap/completed/slice-11a-ability-substrate.md`](../../roadmap/completed/slice-11a-ability-substrate.md) · [`../../roadmap/completed/slice-11b-ability-invocation.md`](../../roadmap/completed/slice-11b-ability-invocation.md) — as-built history and decisions.
- **Effects** — abilities produce effects via `EffectSystem.Apply`; passive bonuses fold in through the `IEffectContributor` seam ([`../effects/effect-system.md`](../effects/effect-system.md)).
- **Combat** — offensive abilities route damage through `ICombatSystem.ResolveAbilityStrike`; kills reach the same death path as melee ([`../combat/combat.md`](../combat/combat.md)).
