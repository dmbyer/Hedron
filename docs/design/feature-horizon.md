# Feature Horizon — Long-Range Brainstorm

> **Status: brainstorm / menu of possibilities. Not authoritative, not a commitment, not a plan.**
> This is a wide, deliberately granular catalog of features common and uncommon in MUDs, plus
> some signature ideas specific to Hedron's themes. It exists to **feed the roadmap** — items
> here graduate into [`../roadmap/plan.md`](../roadmap/plan.md) slices and
> [`../implementation-plans/`](../implementation-plans/) docs through the normal per-slice loop. Nothing here is
> designed to spec depth; each entry is a few sentences plus a rough read on cost, payoff, and
> how well it survives a text / light-web interface.
>
> Same posture as [`gameplay-model.md`](gameplay-model.md) and the `*-planned.md` reference files:
> design *intent*, explicitly not a claim that any of this exists or is scheduled. Where an item
> maps onto an existing gameplay-model **spine** (A Aspect · B Ability · C Effect · D Scaling ·
> E Progression · F Registry), it is tagged so the seam is visible. The point of the spine model
> is that most of these features are *instances of existing primitives*, not new systems.

---

## How to read this

Each feature carries a one-line meta tag:

`Complexity · Value · Text-UI · [spine/status]`

- **Complexity** — engineering effort + architectural risk: **Low / Med / High / Very High**.
- **Value** — gameplay payoff / draw / retention: **Low / Med / High / Very High**.
- **Text-UI** — how well it survives a telnet / light-web interface:
  - **✅ Native** — text *is* the ideal medium (chat, lore, descriptions, naming).
  - **🟢 Good** — clean with ordinary verbs + well-formatted output.
  - **🟡 Workable** — functional, but needs deliberate command/output design or it gets fiddly.
  - **🟠 Clunky** — mechanically rich but awkward in pure text; benefits a lot from web affordances
    (clickable maps, panels, drag-drop, live gauges). Ships fine for telnet but feels second-class.
  - **🔴 Fights the format** — the fun is largely visual/spatial/twitch; reconsider or heavily adapt.
- **[tag]** — `built` / `planned` / `backlog` (already tracked elsewhere) or a spine letter for new work.

The **Text-UI** column is the one to read closely: several high-value features (tactical positioning,
spatial puzzles, real-time anything) are genuinely strong games that turn clunky in a text channel.
Calling that out early prevents building depth the format can't surface.

> **Status grounding.** "built" = exists as of slice 10. "planned" = on [`../roadmap/plan.md`](../roadmap/plan.md)
> or a gameplay-model spine. "backlog" = in [`../roadmap/backlog.md`](../roadmap/backlog.md). Untagged =
> net-new idea not yet tracked anywhere. Verify against the roadmap before scheduling — this doc drifts.

---

## 1. World & Environment

The room graph and movement exist; almost everything that makes a *world* (as opposed to a graph
of rooms) is open. Several of these are already half-scoped in the backlog's "Locale enhancements."

- **Area / zone membership** — Every room belongs to a named area with its own properties (level
  range, PvP rule, respawn rate, ambient theme). Foundational: faction zones, area-scoped effects,
  "you have entered X" banners, and area-level admin all hang off it.
  `Med · High · 🟢 Good · [backlog]`

- **Coordinate system** — `(X, Y, Z)` on rooms, enabling cardinal-distance queries, map generation,
  and "the tower is northeast of here." Cheap to store; unlocks mapping and ranged-line-of-sight later.
  `Low · Med · 🟢 Good · [backlog]`

- **Auto-map / ASCII minimap** — A generated local map drawn from coordinates/exits, shown beside the
  room or via a `map` command. Hugely improves navigation; the single biggest QoL win for newcomers.
  In text it's a boxed ASCII grid; in a web client it becomes a real clickable map.
  `Med · High · 🟡 Workable (🟢 in web) · [new]`

- **Room flags** — Per-room booleans/enums: indoor/outdoor, dark, no-magic, no-recall, safe (no
  combat), no-summon, silent, underwater. Gate behavior cheaply and define zone character. Pure data
  + checks; reads naturally as "It is too dark to see" / "Magic fizzles here."
  `Low · High · 🟢 Good · [new]`

- **Terrain types & movement cost** — Each room/exit has terrain (road, forest, swamp, mountain,
  water) affecting move speed, stamina drain, and which skills apply. Adds texture and makes travel a
  resource decision rather than free teleport-by-typing.
  `Med · Med · 🟢 Good · [new]`

- **Lighting & vision** — Light level per room (time-of-day + light sources + magic); without light a
  player sees little. Drives torches/lanterns, darkvision attunement, stealth in shadow. Rich and
  thematic, but be careful: "you see nothing" repeated is frustrating, so tune the failure text.
  `Med · Med · 🟡 Workable · [new]`

- **Day / night cycle** — Server clock with dawn/day/dusk/night phases that tint descriptions, gate
  NPC schedules, and modify lighting/spawns. Heartbeat already exists to drive it. Strong ambiance for
  low cost; nocturnal mobs and "shops close at night" fall out of it.
  `Low · Med · ✅ Native · [new]`

- **Weather system** — Per-area weather state (clear/rain/storm/fog/snow) on a cycle, affecting
  visibility, certain skills (fire weaker in rain), and flavor. Pairs with day/night. Output is just
  descriptive lines, so it reads beautifully in text.
  `Med · Med · ✅ Native · [new]`

- **Calendar & seasons** — A longer clock (days, months, seasons, named festivals) above day/night.
  Enables seasonal content, holiday events, crop cycles for farming, and lore dating. Mostly a data
  layer over the heartbeat; value depends on seasonal content existing to use it.
  `Med · Med · ✅ Native · [new]`

- **Dynamic room descriptions** — Descriptions composed from state: time-of-day tint, weather,
  faction control, damage/scorch after events, Aspect theming (Spine A). The Dream-area
  description-rewrite idea in the gameplay model is the exotic end of this. Text's home turf — the
  whole medium *is* description — but composition logic must avoid run-on Frankenstein paragraphs.
  `Med · High · ✅ Native · [A]`

- **Doors, locks & keys** — Exits that can be open/closed/locked, opened with `open`/`close`, picked
  with a skill, or unlocked with a keyed item. Bedrock for dungeons, housing, and security. Classic
  and clean in text; the only subtlety is making exit state visible in the room view.
  `Med · High · 🟢 Good · [new]`

- **Hidden exits & search** — Exits revealed only by `search`, a perception check, or a trigger.
  Rewards exploration and hides secrets/shortcuts. Cheap mechanically; the risk is players never find
  them, so pair with hints.
  `Low · Med · 🟢 Good · [new]`

- **Portals & teleport network** — Fixed or player-placed links between distant rooms (gates,
  runestones, recall stones). Solves travel tedium on a large map; ties to the Aspect/Void theme.
  Trivial as a special exit; design tension is not trivializing the world's geography.
  `Low · Med · 🟢 Good · [new]`

- **Recall / home point** — A `recall` command returning the player to a bound home/temple, usually
  with a cost or cooldown. Standard MUD QoL and a soft death/safety valve. Tiny to build.
  `Low · Med · 🟢 Good · [new]`

- **Environmental hazards** — Rooms or tiles that damage/affect on entry or per tick: lava, deep
  water (drowning + swim skill), poison gas, falling, traps. Periodic effects already exist (Spine C)
  to model the damage. Adds danger geography; telegraph clearly or it reads as unfair.
  `Med · Med · 🟢 Good · [C]`

- **Resource nodes** — Harvestable features in rooms (ore veins, herb patches, fishing spots, trees)
  with depletion + respawn. The world-side input to gathering/crafting (§9). Mostly a spawn-and-deplete
  component; value is fully realized only once crafting consumes the output.
  `Med · Med · 🟢 Good · [new]`

- **Containers in the world** — Chests, barrels, corpses, and bags that hold items, can be locked,
  and may be lootable/persistent. Inventory exists; this is nesting + access rules. Underpins loot,
  storage, and housing. Watch nested-container depth and weight rules.
  `Med · High · 🟢 Good · [new]`

- **Item decay / ground cleanup** — Dropped items and corpses decay or get swept after a timer so the
  world doesn't accumulate litter. An operational necessity once items are plentiful. Pure heartbeat
  bookkeeping; invisible when done right.
  `Low · Med · 🟢 Good · [new]`

- **Instancing** — Private per-player/per-group copies of an area (dungeon, story room, housing).
  Enables non-competitive content and the "cursed players enter more dangerous instances" hook in the
  gameplay model. Architecturally significant: the one-world model (INV-12) must accommodate scoped
  sub-worlds. High payoff, real design cost.
  `Very High · High · 🟢 Good · [D]`

- **Procedural / generated areas** — Algorithmically built dungeons/overworld from templates +
  Aspect + rarity bias (Spine D). Near-infinite content from finite authoring; the endgame for the
  generation spine. Big build; sequence it well after the spines it consumes exist.
  `Very High · High · 🟢 Good · [D, E]`

- **Overworld / wilderness travel** — A coarse large-scale map layer above room-level detail for
  long journeys, with encounters and fast-travel waypoints. Gives scale without authoring every tile.
  Often rendered as an ASCII region map — workable in text, much nicer in web.
  `High · Med · 🟡 Workable (🟢 in web) · [new]`

- **Tracks & trailing** — Mobs/players leave tracks a tracker can `follow`. Enables hunting, fleeing-
  with-pursuit, and ranger fantasy. Small system tied to movement events; niche but flavorful.
  `Med · Low · 🟢 Good · [new]`

---

## 2. Movement & Travel

Six-direction movement is built. These extend *how* and *with whom* you move.

- **Following / group travel** — `follow <player>` so a led group moves together; the leader walks,
  the followers trail. Essential for grouping; cheap. Edge cases (leader enters locked door, follower
  in combat) are the only fiddly part.
  `Low · High · 🟢 Good · [new]`

- **Speedwalk / run paths** — Compress a route into one command (`run 3n2e`) or named paths between
  known points. Major QoL on a large map; trivial to parse. Often a client feature too.
  `Low · Med · 🟢 Good · [new]`

- **Movement modes / stances** — sneak (stealth), walk, run (faster, stamina cost), search-as-you-go.
  Layers risk/resource onto travel and feeds stealth play. Modest state on the mover.
  `Med · Med · 🟢 Good · [new]`

- **Mounts** — Rideable creatures that increase speed/carry and may fight. Ties to taming/pets (§6)
  and the move system (mounted move cost, can't enter some rooms). Beloved feature; moderate plumbing.
  `Med · Med · 🟢 Good · [new]`

- **Vehicles / boats** — Multi-occupant movable "rooms" (ships, carts) for water/overworld travel.
  Conceptually a room that moves and carries occupants. Cool but a notable special case in the world
  model; defer until overworld/water content justifies it.
  `High · Med · 🟡 Workable · [new]`

- **Encumbrance** — Carry weight/bulk limits with penalties (slowed, can't move) when overloaded.
  Makes inventory a decision and gives Body/Stamina a job. Simple sum-and-check; the tuning (and not
  annoying players) is the work.
  `Low · Med · 🟢 Good · [new]`

- **Swimming / climbing / flying** — Skill- or item-gated traversal of water/cliff/air exits. Opens
  vertical and aquatic geography and gates shortcuts behind ability. Small per-mode; pairs with
  terrain and movement modes.
  `Med · Low · 🟢 Good · [new]`

---

## 3. Character Identity & Customization

The game is classless (Aspect/attribute-driven). Identity here is mostly cosmetic + soft mechanical
flavor, which is exactly where text shines.

- **Races / species** — Selectable origins with attribute modifiers, innate Aspect affinities, size,
  and racial abilities/skills. A core identity axis that plugs straight into attributes + Spine A/B.
  High value; main cost is content (each race needs flavor + balance).
  `Med · High · ✅ Native · [A, B]`

- **Backgrounds / origins** — A narrative origin granting a small starting bonus, starting location,
  or quest hook, independent of race. Cheap roleplay texture and onboarding variety.
  `Low · Med · ✅ Native · [new]`

- **Player descriptions** — Player-authored long descriptions shown on `look <player>`, plus
  short-desc/keywords. Pure text, pure roleplay value, almost free. Needs light moderation tooling.
  `Low · Med · ✅ Native · [new]`

- **Titles & honorifics** — Earned or chosen titles shown on who/look (`Aelin the Ascended`). Strong
  status/prestige reward for trivial cost; ties to achievements/ascension.
  `Low · Med · ✅ Native · [new]`

- **Alignment / morality axis** — A good/evil or order/chaos scale shifted by actions, gating content
  and NPC reactions. Adds consequence to choices. Cheap to store; value depends on systems reacting to
  it (faction, quests).
  `Med · Med · 🟢 Good · [new]`

- **Survival needs (hunger / thirst / fatigue)** — Pools that deplete over time and need food/drink/
  rest, with penalties when empty. Gives food/cooking a purpose and grounds the world. Easy to build,
  easy to make tedious — many players resent it, so make it gentle or optional.
  `Med · Low · 🟢 Good · [C]`

- **Aging** — Characters age over play/game-time, with cosmetic or mild mechanical effect. Niche
  flavor; rarely worth more than a stat readout unless tied to a mechanic.
  `Low · Low · ✅ Native · [new]`

- **Pronoun / gender / appearance fields** — Free or chosen identity fields feeding message
  generation ("she draws her sword"). Small but improves every generated sentence's polish.
  `Low · Low · ✅ Native · [new]`

- **Alt / multi-character management** — One account, multiple characters, with a selection screen and
  shared account-level storage/currency. Accounts already exist (slice 5); this is the character-list
  layer + shared-bank decision. High retention value.
  `Med · High · 🟢 Good · [new]`

---

## 4. Abilities, Progression & Advancement

Two tightly-coupled spines live here: **Ability** (Spine B — the player's skills/spells kit, the
headline of planned slice 11) and **Progression** (Spine E — experience tracks, no classic levels).
They are distinct primitives — you *learn and improve* abilities *through* progression — but they read
together so often that cataloging them side by side is clearest. Abilities first, then the growth
machinery that advances them.

### Abilities — skills & spells

- **Skills & spells (one unified kit)** — The player's learned, activatable abilities. In Hedron a
  skill and a spell are **one shape** (Spine B): they differ only by *data* — which pool they draw
  (Stamina/Body for skills, Mana/Mind for spells), which stat governs them, and how they're invoked (a
  skill runs like a command, e.g. `kick`; a spell via `cast <name>`). One `IAbilitySystem` checks
  state/cost/cooldown and produces Effects (Spine C). This is the headline of planned slice 11 and the
  primitive every other ability entry instances — there is no separate `SkillSystem`/`SpellSystem`.
  `High · Very High · 🟢 Good · [B, planned]`

- **Activation modes (active / passive / triggered)** — Every ability is Active (invoked now), Passive
  (applies while known, e.g. "sword master"), or Triggered (fires reactively on a condition, e.g.
  dodge/riposte). The single pipeline covers all three via a field, not three systems; the combat
  defensive reactions (§5) are the Triggered case. Lets one system express a whole varied kit.
  `Med · High · 🟢 Good · [B, C]`

- **Cooldowns & resource costs** — Abilities gate on a resource (Stamina/Mana/Astra) and/or a cooldown
  timer ticked by the heartbeat. The pacing layer that makes ability choice a decision rather than spam.
  Small state on the abilities component; central to combat feel and balance.
  `Med · High · 🟢 Good · [B]`

- **Aspect-specific / attunement-gated abilities** — Abilities tied to an Aspect (Spine A) whose
  damage/effects are aspect-typed and whose power scales with the caster's attunement; some are locked
  behind an attunement or ascension threshold. The intersection of Spines A + B — it turns elemental
  identity into an actual *kit* (fire mages, void-callers) rather than just a damage-type modifier.
  Pure data over the ability + aspect spines; high identity payoff.
  `Med · High · 🟢 Good · [A, B]`

- **Spell components / reagents / foci** — Some abilities consume an item (reagent), require a held
  focus, or need a specific condition (a corpse, a station, an altar). A cost/gating layer that ties
  abilities to the economy and crafting, and makes powerful spells deliberate. Small per-ability data;
  watch that it doesn't become inventory busywork.
  `Med · Med · 🟢 Good · [B]`

- **Long-form magic / rituals** — Multi-step or channeled casting: a cast time during which the caster
  is vulnerable and can be interrupted (by damage, movement, silence), optionally requiring reagents,
  multiple participants, or a specific place/time. Big-payoff effects (summons, teleport networks,
  world-altering or area-wide effects) are gated behind this preparation/vulnerability. Elaborate
  rituals chain into the objective/trigger spine (Spine E). The cast-time loop sits on the heartbeat +
  entity-state; channeling messages read dramatically in text. The risk is downtime feeling like dead
  waiting — keep casts short or interactive.
  `High · High · 🟢 Good · [B, E]`

### Progression & advancement

- **Experience tracks** — Per-score XP/improvement tracks (attributes, abilities, attunements, vitals)
  rather than one global level. The core of Spine E; everything else awards into it. High value,
  central design.
  `High · Very High · 🟢 Good · [E]`

- **Skill-by-use improvement** — Abilities improve through use ("practice makes perfect"): each use
  rolls a chance to advance the track. Classic MUD feel, rewards actually playing your kit. Needs
  anti-grind guards (diminishing returns on trivial targets).
  `Med · High · 🟢 Good · [E]`

- **Trainers & learning** — NPCs that teach abilities/raise skills for currency or practice points,
  gated by attribute/attunement/ascension requirements. The sink that makes the economy and
  progression intersect. Modest; ties to NPCs + economy.
  `Med · Med · 🟢 Good · [B, E]`

- **Practice / training points** — A spendable currency earned on advancement, used to unlock or
  improve abilities — the budget knob distinct from raw XP. Gives leveling a choice rather than
  automatic gains. Light bookkeeping.
  `Low · Med · 🟢 Good · [E]`

- **Talent / perk trees** — Branching unlockable passives/abilities representing build choices.
  Excellent for build identity and replay; the tree itself is data over the ability/effect spines.
  Pure text trees are list-y and hard to visualize — a real clunk point that a web panel fixes.
  `High · High · 🟠 Clunky (🟢 in web) · [B, E]`

- **Ascension tiers** — The 0–6 vertical milestone scalar (gameplay-model R1): completing an ascension
  objective raises the scaling baseline and unlocks content/aspects. Headline long-term goal; gated by
  the objective spine. `IdentityComponent.Tier` is the seed.
  `High · High · 🟢 Good · [E, D]`

- **Mastery / paragon levels** — Post-cap incremental progression (mastery ranks, prestige) for
  endgame players. Retention glue once a cap exists; cheap as another track. Premature before a cap.
  `Med · Med · 🟢 Good · [E]`

- **Achievements** — Tracked milestones ("kill 100 wolves," "ascend," "craft a legendary") with
  rewards/titles. Strong completionist hook; mostly handlers listening to existing events + a log
  component. Reads fine as a list.
  `Med · Med · 🟢 Good · [E]`

- **Attribute respec / retraining** — Spend currency/items to reallocate points. Lowers the cost of
  experimentation and build regret. Easy mechanically; an economy sink and a balance lever.
  `Low · Med · 🟢 Good · [E]`

- **Remort / rebirth** — Reset a capped character back toward the start in exchange for a permanent
  power gain, an unlocked path/race/aspect, or a higher progression ceiling — repeatable as an endgame
  loop. The classic MUD "re-mortal" prestige cycle; it generalizes the death-flavored reincarnation idea
  (§14) into a deliberate advancement choice, and sits below Ascension as a softer, repeatable reset.
  Reuses respec + progression + a "rebirth count" track. The design risk is making the reset feel
  rewarding rather than punishing.
  `Med · Med · 🟢 Good · [E]`

- **Offline / passive progression** — A character keeps advancing a *constrained* activity while logged
  off: a skill trains, a crafting queue completes, a resource ticks, rested-XP accrues. Computed from
  elapsed time on next login and shown as a summary. Strong retention for casual / timezone-spread
  players and a hook to log back in. Modest to build (offline-time accounting + caps), but
  philosophically contentious for a *live* MUD — keep it bounded (a cap, a single track) so presence
  still matters most.
  `Med · Med · 🟢 Good · [E]`

---

## 5. Combat Depth

Melee combat, the heartbeat tick, stat computation, effects, and death/respawn are built. These add
tactical texture on top of the existing round loop.

- **Aspect-typed damage & resistance** — Damage carries an Aspect; resolution applies affinity/resist
  (Spine A). Turns flat damage into a rock-paper-scissors layer and gives attunement meaning. The
  first planned consumer of the Aspect spine; combat already exists to receive it.
  `Med · High · 🟢 Good · [A]`

- **Defensive reactions (parry / dodge / block / riposte)** — Triggered abilities (Spine B) that fire
  on incoming attacks with a stat-scaled chance. Adds defensive build identity beyond raw armor.
  Models cleanly as Triggered effects; output needs to stay readable amid combat spam.
  `Med · High · 🟢 Good · [B, C]`

- **Critical hits** — A chance for amplified damage, scaled by a crit score. Universal combat spice;
  trivial as a derived score + roll. Mostly a tuning concern.
  `Low · Med · 🟢 Good · [C]`

- **Status / control effects (stun, root, snare, silence, blind, fear)** — Combat effects that disable
  or impair, built on the effect + entity-state systems. The substance of tactical combat. Diminishing
  returns / immunity windows are needed to keep PvP sane.
  `Med · High · 🟢 Good · [C]`

- **Threat / aggro & tanking** — Mobs track a threat table and target the highest-threat actor;
  abilities modify threat. The mechanical basis of group roles (tank/heal/dps). Real depth, modest
  state; only meaningful once grouping exists.
  `Med · High · 🟢 Good · [new]`

- **Group combat & assist** — `assist <ally>` to join a target, shared aggro, coordinated focus fire.
  Turns combat social. Sits on grouping + threat. Mostly orchestration.
  `Med · High · 🟢 Good · [new]`

- **Ranged combat & ammunition** — Bows/thrown/spells with range, line-of-sight across coordinates,
  and consumable ammo. Adds a positioning axis. The line-of-sight/range part is where text gets
  awkward (no visual field), so keep range abstract (same-room / adjacent-room).
  `High · Med · 🟡 Workable · [new]`

- **Area-of-effect attacks** — Abilities hitting all hostiles (or all targets) in a room/group. Core
  to spellcasting fantasy and add-clearing; the Effect targeting modes already anticipate Room/Group.
  Output must summarize ("the blast scorches 4 foes") not spam per-target lines.
  `Med · High · 🟢 Good · [B, C]`

- **Positioning / formation** — Front/back rank or melee/ranged positioning within a room affecting
  who can hit whom. Genuine tactical depth, but it's the classic text-combat clunk: spatial nuance
  with no map to see it on. Strong in web with a grid; muddy in telnet.
  `High · Med · 🟠 Clunky · [new]`

- **Combat verbosity / message tuning** — Player config for brief vs. verbose combat output, damage
  numbers on/off, color coding. Not a feature so much as a survival requirement once combat is busy —
  text combat lives or dies on output discipline. Pairs with player config (backlog).
  `Low · High · 🟢 Good · [backlog]`

- **Wimpy / auto-flee** — Auto-flee when HP drops below a configured threshold. Classic safety net,
  reduces unfair deaths. Trivial; a config value checked on the combat tick.
  `Low · Med · 🟢 Good · [new]`

- **Corpse looting & corpse runs** — Death drops a lootable corpse (carrying gear/coin) that must be
  recovered, possibly in a dangerous spot. Adds death stakes and a recovery loop. Pairs with
  death/respawn (built) and containers. A meaningful but loved tension; make corpse-finding fair.
  `Med · Med · 🟢 Good · [new]`

- **Resurrection & revive mechanics** — Ally abilities or NPC services to restore a dead/incapacitated
  player, possibly reducing death penalty. Softens death and gives healers a role. Sits on death +
  abilities.
  `Med · Med · 🟢 Good · [B]`

- **PvP combat** — Player-versus-player with consent/flag rules (see below). The whole social-conflict
  dimension. Mechanically combat already supports it; the *rules* around it are the hard part.
  `Med · High · 🟢 Good · [new]`

- **PvP consent / flagging** — Opt-in flags, zone PvP rules, level-range limits, and safe zones
  governing when PvP is allowed. The policy layer that prevents griefing. More social design than code;
  area flags (§1) carry it.
  `Med · High · 🟢 Good · [new]`

- **Dueling** — Consensual 1v1 with a challenge/accept handshake and no death penalty. A safe outlet
  for competition; small handshake + arena-rules. Good newbie-friendly PvP.
  `Low · Med · 🟢 Good · [new]`

- **Arenas & ladders** — Designated combat zones with matchmaking, ranking, and rewards. Endgame PvP
  retention; builds on dueling + leaderboards. Defer until a PvP population exists.
  `High · Med · 🟢 Good · [new]`

- **Bounties** — Place a reward on a player's head (often for PvP/criminal acts), claimable by killing
  them. Emergent social drama for cheap; ties to economy + crime/alignment.
  `Med · Med · 🟢 Good · [new]`

---

## 6. NPCs & Mob Behavior

Basic mobs spawn but don't act. This is where the world comes alive — and a deep well of work.

- **Wandering / roaming AI** — Mobs move between rooms on the heartbeat within zone bounds. The single
  biggest "world feels alive" upgrade for low cost. The roadmap already names it as the next mob step.
  `Med · High · 🟢 Good · [new]`

- **Aggression & aggro** — Mobs that attack on sight based on alignment/faction/level, with assist
  among kin. Turns the world dangerous and navigable-with-care. Modest; pairs with faction.
  `Med · High · 🟢 Good · [new]`

- **Mob factions & relationships** — Mobs belong to factions that define who they help/attack
  (including the player). Enables "lure the orcs into the goblins" emergent play. Data + a relationship
  matrix; high emergent payoff.
  `Med · High · 🟢 Good · [new]`

- **Mob memory & hunting** — Mobs remember attackers and pursue fleers across rooms (with tracking).
  Makes fleeing a real decision and fights consequential. Modest state; tie to tracks (§1).
  `Med · Med · 🟢 Good · [new]`

- **Pathfinding** — Mobs navigate the room graph toward a target/home rather than random-walking.
  Prerequisite for credible hunting, patrols, and roaming. A* over exits — a contained algorithm with
  broad payoff.
  `Med · Med · 🟢 Good · [new]`

- **Scripted boss encounters** — Mobs with phases, special abilities, adds, and mechanics (triggers
  off HP%, timers, positioning). The peak-content draw. Built on triggers + abilities + effects; each
  boss is authored content. Avoid mechanics that need a visual field (see positioning clunk).
  `High · High · 🟢 Good · [B, C, E]`

- **Mob dialogue / conversation** — `talk`/`ask <npc> about <topic>` keyword trees or branching
  dialogue. The connective tissue for quests and lore; text's native strength. Authoring volume is the
  cost, not the engine.
  `Med · High · ✅ Native · [new]`

- **Shopkeepers** — NPCs that buy/sell from inventory (see Economy §8). A specific NPC behavior +
  the trade backend. Foundational to the economy slice.
  `Med · High · 🟢 Good · [planned]`

- **Pets / companions** — Player-owned creatures that follow and assist in combat, with their own
  HP/abilities and maybe loyalty/feeding. Beloved; substantial (a second entity the player commands).
  Command surface (`pet attack`, `pet stay`) is fiddly but workable.
  `High · High · 🟡 Workable · [new]`

- **Charm / summon / conjure** — Abilities that temporarily create or control creatures fighting for
  the caster. Build-defining for some Aspects; sits on abilities + the pet command surface. Cap counts
  to avoid runaway armies.
  `High · Med · 🟡 Workable · [B]`

- **Taming** — Convert a wild creature into a mount/pet via a skill mini-process. Feeds mounts + pets;
  a satisfying progression loop. Moderate; needs the pet system first.
  `Med · Med · 🟢 Good · [new]`

- **Guards & law NPCs** — Faction guards that enforce safe zones, attack criminals (alignment/bounty),
  and respond to crime. The enforcement arm of PvP/crime rules. Ties many systems together.
  `Med · Med · 🟢 Good · [new]`

- **Population control / spawn ecology** — Zone-level caps, respawn timers, and optionally predator/
  prey dynamics so populations feel organic rather than static respawns. The exotic version (true
  ecology) is a signature idea; the basic version (timers + caps) is table stakes.
  `Med · Med · 🟢 Good · [new]`

- **Mob loadouts & loot tables** — Mobs carry/drop equipment and roll loot from weighted tables scaled
  by rarity (Spine D). The reward engine of combat. Loot tables + scaling are the substance; ties
  directly to the economy and gearing loops.
  `Med · High · 🟢 Good · [D]`

---

## 7. Items & Equipment Depth

Items, inventory, equipment, and worn slots are built. These deepen what items *are* and *do*.

- **Item rarity & affixes** — Items roll a rarity tier (Standard…Champion) that grants affixes, which
  *are* Effects (Spine C + D). The core gear-chase loop. Sits squarely on two spines; high value, and
  the scaling transform is reused from mobs.
  `High · Very High · 🟢 Good · [D, C]`

- **Item identification** — Magic items start unidentified; an ability/NPC/scroll reveals their
  properties. Adds mystery and a gold sink. Small flag + reveal action; classic and cheap.
  `Low · Med · 🟢 Good · [new]`

- **Durability & repair** — Gear wears with use and breaks; repaired by NPCs or smithing. A money/
  material sink and a reason to engage crafting. Easy to build, contentious with players — make repair
  cheap/convenient or it's pure friction.
  `Med · Low · 🟢 Good · [new]`

- **Enchanting / imbuing** — Add Effects to existing gear via a process (reagents + chance + risk of
  loss). A deep itemization and economy sink; pure Spine C application. The risk/reward dial is the
  design.
  `Med · High · 🟢 Good · [C]`

- **Sockets & gems** — Items have slots; players insert gems that grant Effects, swappable later. A
  flexible, player-driven itemization layer. Composite effects (Spine C) model it; UI is list-y but
  fine.
  `Med · Med · 🟢 Good · [C]`

- **Item sets & set bonuses** — Wearing N pieces of a set grants escalating bonuses (a composite
  effect keyed on equipped count). Strong build-target content; the `Group`/composite effect concept
  already covers it. Mostly authoring + an equipped-count check.
  `Med · High · 🟢 Good · [C]`

- **Stacking & quantity** — Identical consumables/materials stack with a count rather than N inventory
  slots. A QoL necessity once consumables/materials exist; touches inventory + display. Do it before
  crafting floods inventories.
  `Med · High · 🟢 Good · [new]`

- **Consumables** — Single-use items with effects: food, drink, scrolls, bandages, throwables. The
  everyday-use item class; trivial once the effect spine applies on `use`. Volume is content.
  `Low · Med · 🟢 Good · [C]`

- **Potions** — Drinkable timed/instant effects; explicitly a planned slice (13) and the canonical
  Spine C consumer (instant heal + timed buff). The reference example for "feature = effect instance."
  `Low · Med · 🟢 Good · [C, planned]`

- **Charged items (wands / staves)** — Items with N charges that cast an ability, rechargeable or
  disposable. Gives non-casters access to abilities; sits on abilities + a charge counter.
  `Med · Med · 🟢 Good · [B]`

- **Light sources with fuel** — Torches/lanterns that burn down and must be lit/refueled, gating
  vision (§1). Thematic survival texture; small, but only matters if lighting matters.
  `Low · Low · 🟢 Good · [new]`

- **Item flags** — no-drop, no-sell, bind-on-pickup/equip (soulbound), cursed (can't remove), quest.
  Cheap booleans that enable reward design and economy control. Foundational for itemization policy.
  `Low · Med · 🟢 Good · [new]`

- **Readable items / books / lore** — Items with text content shown on `read`. Pure text, pure
  worldbuilding, nearly free, and a great breadcrumb for quests/lore. Authoring is the only cost.
  `Low · Med · ✅ Native · [new]`

- **Item customization / naming** — Let players rename or inscribe gear (with limits). Cheap
  attachment/prestige; needs moderation. Pairs with crafted-item signatures.
  `Low · Low · ✅ Native · [new]`

- **Salvage / disenchant** — Break items into materials/essence for crafting/enchanting. Closes the
  gear loop (junk → materials) and is an economy sink. Ties items ↔ crafting.
  `Med · Med · 🟢 Good · [new]`

---

## 8. Economy & Trade

Shopping is a planned slice (12). It fans out into many sub-features that each deserve their own seam.

- **Currency** — One or more denominations (copper/silver/gold) or currency types (gold + tokens +
  faction marks). The medium every transaction needs; decide single vs. multi early. Small but
  foundational. `Low · High · 🟢 Good · [planned]`

- **Vendor buy/sell** — Buy from and sell to shopkeeper inventories at set prices. The core economy
  verb pair. Modest; the planned shopping slice's spine. `Med · High · 🟢 Good · [planned]`

- **Buyback** — Re-purchase recently-sold items from a vendor (mistake protection). Small per-session
  list on the vendor; pure QoL but expected. `Low · Med · 🟢 Good · [planned]`

- **Dynamic pricing / supply-demand** — Prices shift with stock, player activity, or events. Makes the
  economy feel alive and creates arbitrage. Real complexity and balance risk; defer until a stable base
  economy exists. `High · Med · 🟢 Good · [new]`

- **Haggling** — A skill/attribute (Mind?) check that shifts buy/sell prices. Gives social stats a
  combat-free use. Small; risks being a mandatory chore if the swing is large. `Low · Low · 🟢 Good · [new]`

- **Banks / vault storage** — Secure storage for items and money beyond inventory, possibly per-area or
  account-wide. Solves carry limits and alt-sharing; a storage container with access rules.
  `Med · High · 🟢 Good · [new]`

- **Player-to-player trade** — A secure two-party `trade` window (offer/confirm) preventing scams. The
  bedrock of a player economy; the confirm handshake is the careful part. `Med · High · 🟢 Good · [new]`

- **Player shops / vendors** — Players set items for sale at a stall/vendor others browse and buy while
  the owner is offline. Drives a real economy and merchant playstyles. Substantial (offline ownership,
  persistence, browse UI). `High · Med · 🟡 Workable · [new]`

- **Auction house** — A central listing/bid/buyout marketplace. The economy's beating heart at scale;
  also significant (search, bids, expiry, delivery). In text, browsing/searching listings is the
  clunk point — a web table is far better. `High · Med · 🟠 Clunky (🟢 in web) · [new]`

- **Mail / parcels** — Send items/money to offline players, with optional COD. Asynchronous economy +
  social glue; ties to player shops/auctions for delivery. `Med · Med · 🟢 Good · [new]`

- **Gambling / games of chance** — Dice, cards, lottery, slot NPCs as currency sinks and social hubs.
  Cheap, fun, and a sink; uses the dice/random spine. Watch the sink/faucet balance. `Low · Low · 🟢 Good · [new]`

- **Crime & theft** — `steal` (pickpocket skill vs. target), fencing stolen goods, consequences
  (guards, bounty, alignment). A whole rogue playstyle and emergent conflict; ties to guards + bounty +
  alignment. Contentious in PvP — gate carefully. `Med · Med · 🟢 Good · [new]`

- **Job board / commissions** — NPC- or player-posted tasks (deliver X, craft Y) with rewards. Bridges
  economy and quests; generated jobs reuse the objective spine. `Med · Med · 🟢 Good · [E]`

- **Economy sinks & faucets monitoring** — Admin instrumentation tracking currency creation/destruction
  to spot inflation. Not player-facing but essential to keep the economy from collapsing. An admin
  metric, not a feature per se. `Med · Med · 🟢 Good · [admin]`

---

## 9. Crafting & Gathering

Crafting is a planned slice (13). The user's example — "crafting also means farming, materials" — is
exactly right; here is the full fan-out.

- **Recipes / blueprints** — Definitions mapping inputs → outputs with skill/station requirements.
  The data spine of all crafting; a registry of recipe definitions (Spine F shape). `Med · High · 🟢 Good · [F]`

- **Materials / reagents** — The intermediate item class crafting consumes (ores, herbs, hides, essences).
  Needs stacking (§7) and gathering (below). The connective substance; mostly item content. `Low · High · 🟢 Good · [new]`

- **Crafting skill progression** — Per-discipline skill tracks that improve with crafting and gate
  recipes/quality. Sits on Spine E; the progression loop that makes crafting a career. `Med · Med · 🟢 Good · [E]`

- **Crafting quality / critical crafts** — Output quality varies by skill + roll (sometimes producing
  rarity/affixes via Spine D). Turns crafting from deterministic to aspirational. Ties crafting to the
  scaling spine. `Med · Med · 🟢 Good · [D]`

- **Crafting stations** — Room-bound stations (forge, loom, alembic) required for certain recipes.
  Anchors crafting to places (and housing/guild halls). A room feature + recipe requirement. `Low · Med · 🟢 Good · [new]`

- **Mining** — Extract ore/gems from resource nodes (§1) with a skill + tool. One gathering profession;
  feeds smithing/jewelcrafting. Small atop nodes. `Med · Med · 🟢 Good · [new]`

- **Herbalism / foraging** — Gather plants/reagents from nodes for alchemy/cooking. Parallel gathering
  profession; same node mechanic, different output. `Med · Med · 🟢 Good · [new]`

- **Logging / skinning / fishing** — Wood from trees, hides from corpses, fish from water spots. Each is
  a gathering verb feeding a craft. Cheap individually; volume of professions is the spend. `Med · Med · 🟢 Good · [new]`

- **Farming** — Plant seeds in plots/rooms, wait real/game-time through growth stages, harvest crops.
  A patient resource loop feeding cooking/alchemy; ties to calendar/seasons. Genuinely fun for a
  subset; the waiting + plot persistence is the design. `High · Med · 🟢 Good · [new]`

- **Cooking** — Combine ingredients into food granting (often timed) buffs; feeds hunger if present.
  A welcoming entry craft; pure Spine C on consume. `Med · Med · 🟢 Good · [C]`

- **Alchemy / potion brewing** — Turn reagents into potions (the §7 potions, Spine C). The canonical
  buff/heal economy. Sits on recipes + effects. `Med · Med · 🟢 Good · [C]`

- **Smithing** — Forge weapons/armor from refined metals, the gear backbone of crafting. Pairs with
  mining → refining → smithing → (enchant). `Med · Med · 🟢 Good · [new]`

- **Tailoring / leatherworking** — Cloth/leather gear from cloth/hides. Parallel to smithing for
  non-metal armor classes. `Med · Med · 🟢 Good · [new]`

- **Refining** — Intermediate processing (ore→ingot, hide→leather, herb→extract) before final craft.
  Adds an economy layer and specialization. A recipe sub-type; optional depth. `Low · Med · 🟢 Good · [new]`

- **Inscription / scribing** — Craft scrolls/runes that cast abilities or enchant. Lets crafters make
  ability items (§7 charged items). Sits on abilities + recipes. `Med · Med · 🟢 Good · [B]`

- **Tools & tool durability** — Gathering/crafting requires tools that may wear out. Adds a material
  sink and a gating item; small, and shares durability (§7). `Low · Low · 🟢 Good · [new]`

- **Recipe discovery / experimentation** — Unlock recipes by finding them, buying them, or combining
  materials to "discover" results. Adds exploration/mastery to crafting beyond a fixed list. Fun but a
  notable design+content investment. `High · Med · 🟢 Good · [new]`

- **Commissions / made-to-order** — A player requests a craft (providing materials/fee); a crafter
  fulfills it. The social face of crafting; reuses job board + trade. `Med · Med · 🟢 Good · [new]`

---

## 10. Social & Communication

`say` is built; broadcast channels are explicitly backlogged. Text MUDs are fundamentally social
software, so most of this is **native** to the medium and high-value-per-cost.

- **Tell / private message** — `tell <player> <msg>` cross-room direct message, with reply. The most
  requested social verb after say. Tiny; pairs with the backlog's arg-redaction note for privacy.
  `Low · High · ✅ Native · [new]`

- **Channels** — Global/newbie/trade/OOC broadcast channels with membership. Explicitly backlogged
  (needs per-entity channel-membership state). The social backbone of a live MUD. `Med · High · ✅ Native · [backlog]`

- **Channel management** — Join/leave, mute, per-channel history/replay, moderation. The control layer
  that keeps channels usable; ships alongside channels. `Med · Med · ✅ Native · [backlog]`

- **Emotes (predefined socials)** — A library of `smile`, `bow`, `wave <target>` socials with first/
  second/third-person message templates. The heart of MUD roleplay flavor; a content table + a
  template renderer. Cheap, beloved. `Med · High · ✅ Native · [new]`

- **Freeform emote / pose** — `emote <freeform>` ("Aelin sharpens her blade, eyeing the door"). Pure
  expressive roleplay; trivial to build. `Low · Med · ✅ Native · [new]`

- **Whisper / yell** — Room-scoped private whisper and area-wide yell, between say and channels in
  scope. Small additions to the broadcast system. `Low · Med · ✅ Native · [new]`

- **Languages** — Racial/learnable languages; speech is garbled to those who don't know it. Deep
  roleplay/immersion layer over say/tell; a per-message transform + a known-languages component.
  Lovely flavor, niche payoff. `Med · Low · ✅ Native · [A?]`

- **Who list** — `who` shows online players with title/flags/level. Table-stakes social presence;
  trivial. `Low · Med · ✅ Native · [new]`

- **Finger / character lookup** — `finger <name>` shows public profile (title, last login, bio).
  Social discovery; small read over account/character data. `Low · Low · ✅ Native · [new]`

- **Friends & ignore lists** — Track friends (online alerts) and block harassers (mute/hide). Both a
  social and a safety feature; per-account lists. Ignore in particular is a moderation necessity.
  `Med · Med · ✅ Native · [new]`

- **Bulletin boards / in-game forums** — Persistent post/read boards in rooms for notices, RP, and
  org coordination. Async community space; a persistent message store + read/post verbs. `Med · Med · ✅ Native · [new]`

- **Player notes / journal** — Private per-character notes/quest log scratchpad. Cheap QoL; pairs with
  the quest journal. `Low · Low · ✅ Native · [new]`

- **Roleplay tools** — Mood/description switching, OOC brackets, RP-consent flags. Signals to a
  roleplay community that they're supported; small, high value to that audience. `Low · Med · ✅ Native · [new]`

- **Mentor / newbie help** — A `newbie` channel + a way for veterans to assist newcomers (summon/teleport
  to help, mentor flag). Retention-critical onboarding social glue. Builds on channels + teleport.
  `Med · Med · ✅ Native · [new]`

- **Discord / external chat bridge** — Relay in-game broadcast channels to and from a Discord server,
  plus presence ("12 online"), event/level/death notifications, and optionally a few read-only commands
  (`who`, `status`) issued from Discord. Where modern MUD communities actually live; it massively boosts
  cohesion and pulls offline players back. The relay rides the existing broadcast/event bus, but the
  integration itself is an external service + bot living *outside* the engine's transport — keep it
  behind the same `ISession` / event seam so it never leaks into core. Text in, text out: native
  content, external plumbing.
  `High · High · ✅ Native (external) · [new]`

---

## 11. Groups, Guilds & Factions

Multiplayer organization. Grouping is near-term-valuable; guilds are a larger meta-layer.

- **Party / group** — Form a temporary group (`group <player>`, invite/accept) with shared status
  visibility. The unit of cooperative play; prerequisite for group XP/loot/combat. `Med · High · 🟢 Good · [new]`

- **Group XP sharing** — Distribute kill XP among nearby group members (by contribution or evenly).
  Makes grouping rewarding rather than a loss. Small rule on the XP award path (Spine E). `Low · High · 🟢 Good · [E]`

- **Group loot rules** — Round-robin, free-for-all, need/greed rolls, master looter. Prevents loot
  drama; a policy applied at the drop. The need/greed roll UI is mildly fiddly in text but workable.
  `Med · Med · 🟡 Workable · [new]`

- **Group leader controls** — Promote, kick, set loot rule, mark targets. The admin surface of a group;
  small command set. `Low · Med · 🟢 Good · [new]`

- **Guilds / clans** — Persistent player organizations with membership, ranks, and a roster. The
  long-term social meta-structure and retention anchor. Substantial: persistence, ranks, permissions.
  `High · High · 🟢 Good · [new]`

- **Guild ranks & permissions** — Hierarchical roles gating guild bank/invite/promote. The governance
  layer; a permission matrix over guild actions. `Med · Med · 🟢 Good · [new]`

- **Guild bank / storage** — Shared guild vault with rank-gated access. Cooperative economy; a shared
  container with an audit log (theft risk). `Med · Med · 🟢 Good · [new]`

- **Guild halls / housing** — A guild-owned area (see housing §13) for gathering, storage, crafting
  stations. The guild's physical home; reuses housing + instancing. `High · Med · 🟢 Good · [new]`

- **Guild progression / perks** — Guild-wide XP/levels unlocking perks (buffs, bank slots, hall rooms).
  Gives the guild a shared goal; another track (Spine E). `Med · Med · 🟢 Good · [E]`

- **Player factions / reputation** — Standing with NPC factions, shifted by actions, gating vendors/
  quests/aggro. A core RPG progression axis distinct from guilds; ties to alignment, mob factions, and
  quests. `Med · High · 🟢 Good · [new]`

- **Guild wars / rivalries** — Declared PvP conflict between guilds with scoring/objectives. Endgame
  social-conflict content; sits on PvP + guilds + objectives. Defer until both populations and PvP
  exist. `High · Med · 🟢 Good · [new]`

- **Alliances / diplomacy** — Formal friendly/hostile relations between guilds/factions. The diplomacy
  layer above wars; mostly a relationship matrix + the social drama it enables. `Med · Low · 🟢 Good · [new]`

---

## 12. Quests, Objectives & World Content

Spine E (objectives/quests/triggers) is the planned engine. These are its surface features.

- **Quest log / journal** — Track active/completed objectives with progress and hints. The player's
  view into the objective system; a log component + display. Onboarding-critical. `Med · High · 🟢 Good · [E]`

- **Quest givers & turn-ins** — NPCs that offer objectives and accept completion for rewards. The
  delivery surface of quests; ties dialogue (§6) + objectives. `Med · High · 🟢 Good · [E]`

- **Quest chains** — Sequenced quests telling a story, each unlocking the next. Narrative backbone;
  pure objective chaining. Authoring is the cost. `Med · High · ✅ Native · [E]`

- **Collection / kill / delivery objectives** — The objective condition kinds (KillMob×N, CollectItem×N,
  EnterRoom, Deliver). The atoms quests are built from; explicitly the Spine E condition set.
  `Med · High · 🟢 Good · [E]`

- **Repeatable / daily / weekly quests** — Objectives on a cooldown for steady rewards. Retention loops;
  a DailyCooldown flag (named in the gameplay model). `Low · Med · 🟢 Good · [E]`

- **Dynamic / generated quests** — Objectives generated from templates ("kill N of whatever's nearby").
  Infinite-ish content from finite authoring; same shape, different authoring (gameplay-model E2).
  `High · Med · 🟢 Good · [E]`

- **Branching quests / choices** — Player decisions alter outcome/rewards/reputation. Adds agency and
  replay; the branch-state is the design cost. `High · Med · ✅ Native · [E]`

- **Puzzle rooms / mechanisms** — Lever/statue/sequence puzzles driven by world triggers (the
  statue-dragging example in the gameplay model). Great exploration content; built on triggers +
  objective PuzzleState. Spatial puzzles can get clunky without a map. `High · Med · 🟡 Workable · [E]`

- **Riddles & lore puzzles** — Text riddles answered by saying/typing a solution. Text's home turf,
  nearly free, and memorable. Authoring + anti-spoiler is the only cost. `Low · Med · ✅ Native · [E]`

- **World events / dynamic events** — Scheduled or triggered area-wide happenings (invasion, boss
  spawn, meteor shower) players rally to. Big "live world" energy; sits on triggers + scheduling +
  broadcast. `High · High · 🟢 Good · [E]`

- **Seasonal / holiday events** — Calendar-gated content (festivals, themed bosses, limited rewards).
  Recurring re-engagement hooks; reuses world events + calendar. Mostly content. `Med · Med · ✅ Native · [E]`

- **Escort / protect objectives** — Guide/defend an NPC through danger. Classic quest variety; needs
  NPC pathfinding + follow. Can be frustrating (escort AI) — a known genre pain point. `Med · Low · 🟢 Good · [E]`

---

## 13. Player Housing & Territory

A meta-layer that gives players a stake in the world. High retention, real architectural weight
(persistence + instancing + per-player content).

- **Player housing** — Personal instanced rooms a player owns, enters, and decorates. A powerful
  attachment/retention feature; depends on instancing + persistent per-player content. `Very High · High · 🟢 Good · [new]`

- **House storage** — Personal storage inside a house (beyond bank). Naturally falls out of housing +
  containers. `Med · Med · 🟢 Good · [new]`

- **Furniture & decoration** — Place/arrange objects in a house for function (storage, crafting station)
  or cosmetics/trophies. The creative-expression payoff of housing; placement commands are list-y in
  text, far nicer in web. `High · Med · 🟠 Clunky (🟢 in web) · [new]`

- **Player building (player dig)** — Let trusted players create/link their own rooms (a sandboxed
  version of admin `dig`/`set`). Powerful UGC; reuses the builder systems with a permission gate.
  Moderation + quota are the risks. `High · Med · 🟢 Good · [new]`

- **Land claims / plots** — Limited ownable plots/addresses in the world for houses/shops. The scarcity
  layer that makes housing meaningful; an ownership + adjacency model. `High · Med · 🟢 Good · [new]`

- **Housing upkeep / rent** — Periodic cost to retain a house (an economy sink and anti-hoarding). Ties
  housing to the economy; gentle or punitive is the dial. `Low · Low · 🟢 Good · [new]`

- **Access control / roommates** — Grant friends/guildmates access to a house/storage. The permission
  layer; small, but needed for shared/guild spaces. `Med · Low · 🟢 Good · [new]`

---

## 14. Death & Consequence (extensions)

Death/respawn shipped in slice 10. These extend the stakes and recovery dimension.

- **Configurable death penalty** — XP loss, durability damage, dropped items, or stat debuff on death,
  tuned per area/difficulty. The dial that sets the game's tension; mostly config + effect application.
  `Med · Med · 🟢 Good · [C]`

- **Resurrection sickness** — A temporary debuff after dying/reviving (an `UntilRemoved`/`Timed` effect).
  Softens corpse-run abuse; a single effect instance. `Low · Med · 🟢 Good · [C]`

- **Soul / ghost mechanics** — Dead players become ghosts who must travel to their corpse/a healer to
  revive. Adds a recovery mini-game and death weight; a death-state + movement rules. Can be tedious —
  keep distances fair. `Med · Med · 🟢 Good · [new]`

- **Permadeath (opt-in / hardcore)** — A character mode where death is final, for a separate ladder/
  prestige. Strong for a hardcore audience; mostly a flag + leaderboard. Niche but cheap. `Low · Med · 🟢 Good · [new]`

- **Reincarnation / rebirth** — Reset a maxed character for a permanent bonus (a soft prestige). Endgame
  retention loop; reuses respec + progression. Premature before a cap. `Med · Med · 🟢 Good · [E]`

---

## 15. Client, UI & Quality of Life

The telnet output framework (ANSI, formatter) is built. These reduce friction and meet players where
they are. The web client is the deferred slice 14. **This section is where the text/UI tension is
sharpest** — much of it exists *because* a raw telnet stream is hostile to newcomers.

- **Prompt customization** — A configurable status prompt (`[HP:80/100 MP:30 >]`) with tokens for
  vitals/state. Universal MUD QoL; a template parsed against live scores. High value per cost; needs
  player config (backlog). `Med · High · 🟢 Good · [backlog]`

- **Player config / settings** — Per-character preferences (combat verbosity, autoloot, autoswap,
  prompt, color on/off). Explicitly backlogged; the umbrella many QoL features need. The single most
  enabling QoL slice. `Med · High · 🟢 Good · [backlog]`

- **Aliases** — Player-defined command shortcuts (`alias kk = kill kobold`). Massive QoL; server-side
  aliases help players on bare clients. A per-character alias map expanded pre-dispatch. `Med · High · 🟢 Good · [new]`

- **Macros / multi-command** — Bind a sequence to one trigger (`;` separators or named macros).
  Power-user efficiency; an extension of aliases. Watch for spam/automation abuse. `Med · Med · 🟢 Good · [new]`

- **Autoloot / autosac / autoassist** — Toggles to auto-perform routine post-kill actions. Removes
  tedium from the core loop; small flags checked by combat/loot handlers. `Low · High · 🟢 Good · [new]`

- **Paging / `more` prompts** — Page long output instead of flooding the screen. A readability necessity
  once output (who, inventory, help) is long; a session-level output buffer. `Med · Med · 🟢 Good · [new]`

- **Tab completion & command history** — Complete verbs/targets and recall prior commands. Big input-
  ergonomics win; partly client-side, partly server hints. `Med · Med · 🟡 Workable · [new]`

- **GMCP / MSDP / ATCP** — Telnet sub-protocols that send structured data (vitals, room, map) to capable
  clients (Mudlet, MUSHclient) for GUI gauges/maps without cluttering the text. The bridge between a
  pure text stream and a rich client experience — high leverage for the modern MUD audience. A protocol
  layer over the session. `High · High · 🟢 Good (enables 🟢 rich clients) · [new]`

- **MXP / clickable links** — Markup making room exits/items/links clickable in supporting clients. Turns
  text into a lightly interactive surface; a formatter extension. Degrades gracefully to plain text.
  `Med · Med · 🟢 Good · [new]`

- **Web client** — A browser client over the same `ISession` contract (deferred slice 14). The single
  biggest reach/accessibility multiplier and the home for every "🟠 clunky in text" feature (maps,
  panels, drag-drop, gauges). Major transport work; the architecture already anticipates it.
  `Very High · Very High · 🟢 Good · [planned]`

- **Help system & in-game wiki** — Searchable, cross-linked help (built: basic `help`/`commands`).
  Onboarding and reference; expand to topic articles + search. Content-heavy but essential. `Med · High · ✅ Native · [built/extend]`

- **Tutorial / onboarding zone** — A guided first-time-player area teaching core verbs. The make-or-break
  first 10 minutes; content + a few scripted triggers. High retention leverage. `Med · High · 🟢 Good · [E]`

- **AFK / idle handling** — `afk` flag, idle timeout, link-dead detection, and graceful reconnect that
  reattaches a session to its entity. Operational necessity for a persistent world; touches session
  lifecycle. The reconnect part is the substance. `Med · High · 🟢 Good · [new]`

- **Session transcript / logging** — Optional server- or client-side logging of a play session. QoL for
  players who want records; mostly client-side, but a server `log` toggle helps. `Low · Low · 🟢 Good · [new]`

- **Color themes / accessibility** — Player-selectable palettes, color-off mode, screen-reader-friendly
  output. Built on the output formatter + config; widens the audience. Important, modest. `Med · Med · 🟢 Good · [backlog]`

---

## 16. Administration, Moderation & Live Ops

The admin substrate (auth, audit, `@spawn`/`@teleport`/`dig`/`set`/`mkitem`/`mkmob`, `@reload`) is built.
These extend the operator and builder toolkit.

- **Builder permission tiers** — A graded immortal/builder hierarchy (builder < admin < owner) scoping
  which commands and which areas each can touch. Extends the existing structural privilege gate; needed
  before opening building to more people. `Med · Med · 🟢 Good · [new]`

- **Online creation (OLC) expansion** — A fuller in-game building toolset (room/mob/item/quest editors)
  beyond the current ad-hoc commands. The content-velocity multiplier; all logic lives in builder/writer
  systems already (backlog note), so this is more verbs over the same systems. `High · High · 🟢 Good · [backlog]`

- **Web content editor** — A browser admin/builder UI over the builder systems (deferred with the
  dual-client transport). Far friendlier content authoring than telnet commands; the backlog explicitly
  keeps authoring logic in systems so this stays a thin client. `Very High · High · 🟢 Good · [backlog]`

- **Moderation tools** — mute, jail, kick, ban (account/IP), with reason + duration + audit. Community-
  safety necessity once there's a population; small commands over session/account state + the audit
  handler. `Med · High · 🟢 Good · [new]`

- **Snoop / possess / impersonate** — Admin observes a player's I/O or controls an NPC for events/support.
  Powerful for support and live GM events; significant privacy/permission care. `Med · Med · 🟢 Good · [new]`

- **Player reports / tickets** — In-game `report`/`bug`/`idea` filing to a queue admins review. Closes the
  player→staff loop; a persistent ticket store + admin review verbs. `Med · Med · ✅ Native · [new]`

- **Wiznet / staff channel** — A staff-only channel for coordination and event/error feeds. Reuses the
  channel system with a privilege gate. `Low · Med · ✅ Native · [backlog]`

- **Metrics & dashboards** — Operator visibility into population, economy faucets/sinks, retention, errors.
  Essential for running (not playing) the game; mostly logging + aggregation, ideally web-surfaced.
  `High · Med · 🟠 Clunky in text (🟢 web) · [admin]`

- **Backup & restore** — Scheduled world-state snapshots + restore tooling. Operational safety net atop
  persistence; partly built (flush cycle), needs scheduling + restore. `Med · High · 🟢 Good · [new]`

- **Live GM events** — Tooling for staff to run spontaneous events (spawn bosses, narrate, reward). The
  human-driven content layer; composes possess + spawn + broadcast + reward. High community payoff.
  `Med · High · 🟢 Good · [new]`

- **Anti-cheat / rate limiting** — Detect/curb automation, spam, and exploits (command rate caps,
  anomaly flags). Health-of-game necessity at scale; touches the dispatcher + session. `Med · Med · 🟢 Good · [new]`

---

## 17. Meta / Cross-Cutting Systems

Engine-level capabilities that many features lean on. Several are already built or planned as spines.

- **Registry layer** — One uniform `IRegistry<TDefinition>` pattern per trait family (Aspects, Stats,
  Abilities, Rarity, Resources, Ascension, Objectives). Spine F; the lookup spine that makes "definitions
  spread across modules" a non-issue. Foundational to nearly everything above. `Med · High · 🟢 Good · [F]`

- **Scaling / rarity transform** — One spawn-time `ScalingTreatment` applied to mobs, items, and area
  generation context (Spine D). Reuse incarnate: Veteran wolf, magic sword, "more dangerous cellar" are
  one transform. `High · High · 🟢 Good · [D]`

- **Random generation system** — Seeded RNG + weighted-table infrastructure underpinning loot, crafting
  quality, generation, and dice. A core utility many systems compose; modest but pervasive. `Med · High · 🟢 Good · [new]`

- **Scheduling / cron events** — Time-based scheduled triggers (respawns, daily resets, events, weather)
  above the raw heartbeat. The engine for daily/world-event content; a scheduler over the time system.
  `Med · Med · 🟢 Good · [E]`

- **Leaderboards / rankings** — Tracked rankings (richest, most kills, fastest ascension) surfaced in-game.
  Competition/prestige hook; an aggregation + display. Reads fine as a list. `Med · Med · 🟢 Good · [new]`

- **Hall of fame / memorials** — Persistent recognition of notable achievements/retired characters.
  Cheap prestige + community history; a persistent record + display. `Low · Low · ✅ Native · [new]`

- **In-game news / changelog** — A `news` command surfacing updates/patch notes/events. Keeps players
  informed without leaving; a simple post store. `Low · Low · ✅ Native · [new]`

- **Seasons / competitive resets** — Periodic ladder/economy resets with rewards (a fresh-start mode).
  Re-engagement at scale; significant (parallel world-state). Far-future. `Very High · Med · 🟢 Good · [new]`

---

## 18. Signature / Unconventional Ideas

Less-common mechanics, several leaning on Hedron's specific themes (Aspects, Ascension, the named
Aspects like Dream/Mirror/Void). These are differentiators — higher risk, higher identity payoff.

- **Aspect attunement drift** — A player's affinities slowly shift toward the Aspects they use/are
  exposed to (cast fire → grow fire affinity; linger in a Void area → drift Void). Makes identity
  emergent from play rather than chosen. Sits on Spine A + E. The feedback loop is the design risk
  (runaway specialization). `High · High · 🟢 Good · [A, E]`

- **Dream-area description rewriting** — In Dream-Aspect areas, room descriptions and exits subtly,
  non-deterministically rewrite (the gameplay model's headline exotic example). Unsettling, memorable,
  text-native magic. A `Trigger` effect on the area; the trick is staying coherent. `High · Med · ✅ Native · [A, C]`

- **Mirror-Aspect doubles** — Mirror areas/effects spawn a hostile copy of the player (mirrored
  stats/abilities) to fight. A self-reflective signature encounter; reuses spawn + scaling from the
  player's own scores. `High · Med · 🟢 Good · [A, D]`

- **Curse-biased generation** — A cursed player's `TransformModifier` biases generated instances toward
  higher rarity/danger (the gameplay model's "cursed → more dangerous instances"). Risk/reward emergent
  from a debuff; closes the Spine C↔D loop. `High · Med · 🟢 Good · [C, D]`

- **Living ecology** — Mob populations as predator/prey with migration, so over-hunting a species shifts
  the food chain and spawns. Deep emergent "living world"; serious complexity and balance risk, but a
  rare and striking differentiator. `Very High · Med · 🟢 Good · [new]`

- **Knowledge / lore as a mechanic** — Discovered lore (read books, talk to NPCs, find clues) unlocks
  abilities/recipes/dialogue — knowing the world *is* progression. Rewards reading, which text MUDs are
  uniquely good at. A knowledge-flags component + gates. `Med · Med · ✅ Native · [E]`

- **Reputation-driven emergent NPCs** — NPCs react to your specific history (you robbed this town; you
  saved that elder) beyond a faction scalar. Deep immersion; expensive to track and author per-NPC
  memory. The exotic end of faction/reputation. `Very High · Med · 🟢 Good · [new]`

- **Player-run governance** — Players hold offices (mayor, guildmaster) with real powers (set taxes,
  laws, bounties). Emergent politics and player-driven content; sits on guilds + economy + permissions.
  High social payoff, high abuse surface. `High · Med · 🟢 Good · [new]`

- **Asynchronous world consequences** — Player/guild actions permanently alter shared world state (a
  town falls, a gate stays open, a boss stays dead for everyone). Makes the world feel authored-by-its-
  players; tension with new players seeing a "spent" world. `Very High · High · 🟢 Good · [E]`

- **Aspect-resonance crafting** — Crafted item properties depend on the Aspect of the materials, station,
  and crafter attunement — same recipe, different result by elemental context. Deepens crafting along the
  game's signature axis; sits on Spine A + crafting. `High · Med · 🟢 Good · [A]`

- **Ascension world-phasing** — Higher-tier players perceive/access layers of the world lower tiers can't
  (hidden rooms, overlaid Aspect-reality). Makes Ascension change *how you see the world*, not just your
  numbers; reuses room flags + instancing/phasing. Striking, architecturally heavy. `Very High · High · 🟢 Good · [E, D]`

- **Permadeath legacy / inheritance** — On a hardcore character's death, a successor inherits a fraction
  of progress/items (a heirloom). Softens permadeath into a generational loop; reuses permadeath +
  account storage. `Med · Med · 🟢 Good · [new]`

- **Rogue-like run mode** — An opt-in alternate mode: enter a procedurally-generated,
  escalating-difficulty gauntlet (instanced, Spine D generation) with permadeath, where the *run* is
  disposable but a *meta-progression* persists between runs (unlock starting boons, banked currency, new
  starting aspects). Much of the roguelike DNA already appears scattered as engine features — procedural
  areas (§1), instancing, item identification (§7), permadeath (§14) — so this entry is really about
  *packaging* them as a self-contained loop with its own meta-track (Spine E). Great for solo and
  bite-sized sessions, and a strong differentiator. Text roguelikes are a venerable genre, so the format
  fits well; the spend is the generation + meta-progression scaffolding.
  `High · High · 🟢 Good · [D, E]`

---

## How this feeds the roadmap

This catalog is a **menu, not a plan**. To turn an entry into work:

1. Confirm it isn't already tracked (check [`../roadmap/plan.md`](../roadmap/plan.md) and
   [`../roadmap/backlog.md`](../roadmap/backlog.md) — the `[built]`/`[planned]`/`[backlog]` tags here
   are a starting hint, not authoritative).
2. Note which **spine** it instances (A–F). Most of §4–§14 are *instances of existing primitives*, not
   new systems — that is the whole point of [`gameplay-model.md`](gameplay-model.md). A new entry that
   needs a genuinely new primitive is the rare, reviewed exception.
3. Run the `new-plan` skill / `implementation-planner` to produce a use-case doc, then the normal
   per-slice loop (spec gate → implement → code gate → sync-roadmap).

### Natural clustering (suggested reading, not a commitment)

Some features only pay off in groups; building one without its neighbors leaves it stranded:

- **"World comes alive" cluster** — area membership + room flags + wandering/aggro AI + day-night +
  dynamic descriptions. Each is modest; together they transform the feel. Strong near-term value.
- **"Social MUD" cluster** — tell + channels + emotes + who + friends/ignore. Almost all ✅ Native,
  high value per cost, and overdue for a text game. Several already backlogged.
- **"Gear chase" cluster** — rarity/affixes (D+C) + loot tables + identification + salvage + sets.
  Closes the combat→reward→power loop; depends on the Scaling spine.
- **"Abilities & magic" cluster** — skills/spells (B) + aspect-typed damage (A) + status/control
  effects (C) + cooldowns/resources. The combat-and-utility kit and where most "class fantasy" lives;
  this is planned slice 11's spine. Depends on the Ability + Effect + Aspect spines.
- **"Player economy" cluster** — currency + vendors + bank + trade + mail. The shopping slice (12) is
  the seam; the rest fan out from it. Player shops/auction are a later, heavier tier.
- **"Crafting career" cluster** — recipes + materials + gathering (mining/herbalism) + a craft or two
  (alchemy/smithing) + stations. The slice-13 seam; farming and discovery are later depth.
- **"QoL / retention" cluster** — player config + prompt + aliases + autoloot + paging + onboarding
  zone. Low individual cost, high retention; player config (backlog) unlocks most of it.

### Watch-list: features that fight the format

Flagged 🟠/🔴 above — rich games that turn clunky in text. Build the depth only with a plan to surface
it (usually the web client, slice 14):

- Tactical positioning / formation combat (🟠) — spatial nuance with no visible field.
- Auction house browsing (🟠) — search/sort/compare wants a table, not scrollback.
- Talent/perk trees (🟠) — branching structure is hard to read as a text list.
- Housing decoration / furniture placement (🟠) — arrangement wants drag-drop.
- Auto-map / overworld (🟡→🟢 in web) — usable as ASCII, far better rendered.
- Metrics dashboards (🟠, admin) — aggregation wants charts.

These aren't "don't build" — they're "build aware that the telnet version is the floor and the web
client is where they shine."

---

## Related

- [`gameplay-model.md`](gameplay-model.md) — the spine model (A–F) most of these features instance.
- [`../roadmap/plan.md`](../roadmap/plan.md) — current focus + phase strategy (what's actually scheduled).
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — deferred work already tracked (don't re-brainstorm it).
- [`../reference/components-planned.md`](../reference/components-planned.md) /
  [`../reference/systems-planned.md`](../reference/systems-planned.md) — idealized APIs for unbuilt pieces.
- [`../implementation-plans/README.md`](../implementation-plans/README.md) — the per-slice behavior-spec format these graduate into.
