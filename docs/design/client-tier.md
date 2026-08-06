# Client Tier — Blazor vs. React + SignalR

> **Status: active architecture decision with a scheduled gate.** This is a cross-cutting forward
> design model in the sense of [`09-documentation.md`](../architecture/09-documentation.md) — it spans
> many future slices and precedes their implementation plans. It records a **direction** and a
> **deferral**, not a commitment to build. The *current* web tier's as-built design is
> [`../architecture/08-blazor.md`](../architecture/08-blazor.md); this doc governs where that tier goes next.
>
> Decided 2026-08-05 from an adversarial two-agent analysis (positions argued separately, then
> refereed against the code). Every quantitative claim below was verified against the tree at
> `40e3ff9`; the file:line citations are the evidence, not illustration.

---

## The question

The Blazor Server authoring editor ([`Hedron.Web/`](../../Hedron.Web/), ~4,790 LOC / 26 files) works but
is clunky for content creation. That prompts a larger one: the [three-suite end-state](../architecture/08-blazor.md#the-three-suite-end-state-forward-design)
commits the eventual **player client** to this same Blazor host. Should the project instead move to a
React SPA over SignalR — for the client, and for the editor that would otherwise be a second stack?

## The decision

**Long-run direction: React for the player client, with the editor following it. Near-run: do not
start the migration.** The fork is deferred to a scheduled gate at the Phase 5 → Phase 6 boundary
(see [Decision gate](#decision-gate)), and until then the project builds only work that is valuable
under *both* outcomes.

Three findings drive the split verdict:

1. **The clunkiness is mostly not Blazor.** ~⅔–⅘ of it is repairable in place — see [Evidence](#evidence-base).
   Framework choice is not what is making the editor slow today.
2. **The circuit model is genuinely wrong for a player client.** A Blazor circuit is a server-side
   render tree per connection; a MUD client is an append-only stream with client-owned scrollback.
   That pressure is real and points at React.
3. **The player client is blocked for *both* routes.** It re-introduces live-world mutation from
   request threads, against the acknowledged `ComponentRepository` debt named in `INV-31`
   ([`checklist.md`](../architecture/checklist.md)) and the open world-state threading decision in
   [`../roadmap/backlog.md`](../roadmap/backlog.md). The thing that would force the fork cannot be built
   yet regardless of framework — so the fork is not urgent, and deciding it now means deciding it
   at the point of maximum ignorance.

The deciding contextual fact: **`data/content/` holds three example YAML files and zero authored
world content** (`git ls-files data content`). The editor under debate has never been driven against
a real content set, and Phase 5 exists to produce one. Authoring-friction evidence does not exist yet.

## Evidence base

Findings both analyses reached independently, verified at referee:

| Finding | Evidence | Bearing |
|---|---|---|
| The dominant latency source is an uncached catalog, not the framework | [`ContentDefinitionCatalog.cs:91`](../../Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs) — `List(kind)` scans + deserializes every file per call, plus a second full room scan via `BuildRoomAreaMap()`; called from inside a render loop at [`Browser.razor:163`](../../Hedron.Web/Components/Pages/Browser.razor) | An HTTP layer in front of this is **slower**, not faster. Fix it either way. |
| Blazor's own remedies were never reached for | `@key`, `Virtualize`, debounce, `EditForm`/`DataAnnotationsValidator`, `IJSRuntime`: **0 usages each** across 4,790 LOC / 17 touching slices | The framework was not tried before being blamed — but the *reason* it wasn't is itself signal (see [agent fluency](#agent-fluency)). |
| Logic has leaked into components | [`AreaGridEditor.razor:400`](../../Hedron.Web/Components/Pages/AreaGridEditor.razor) does direction math and picks a write policy; [`Standards.razor:383`](../../Hedron.Web/Components/Pages/Standards.razor) constructs a `PowerBudgetSystem` inside a render loop; [`MobEditor.razor:409`](../../Hedron.Web/Components/Pages/MobEditor.razor) does read-modify-write tuple surgery on `CurrencyLoot` | `INV-8` violations. Combined with presentation skip-tier ([`07-testing.md`](../architecture/07-testing.md)), they are **unverifiable** — a genuine hole. |
| A data-loss defect, in all four editors | [`MobEditor.razor:433`](../../Hedron.Web/Components/Pages/MobEditor.razor), [`ItemEditor.razor:336`](../../Hedron.Web/Components/Pages/ItemEditor.razor), [`AreaEditor.razor:176`](../../Hedron.Web/Components/Pages/AreaEditor.razor), [`RoomEditor.razor:127`](../../Hedron.Web/Components/Pages/RoomEditor.razor) — changing the blueprint id rebuilds the template from defaults, silently discarding every other field | Framework-independent. Ports to React verbatim if not fixed. |
| "Apply to live" cannot reach the running game | [`Apply.razor:15-19`](../../Hedron.Web/Components/Pages/Apply.razor) states it: the reload runs in the authoring host's own preview world | The zero-DTO shortcut already cost the editor its headline feature. Fixing it needs a **transport**, not React. |
| The SignalR seam is thinner than claimed | [`IOutputFormatter.cs:18`](../../Core/Output/IOutputFormatter.cs) returns `string`; `ISession.SendLineAsync` takes `string` | `TransportKey` buys another *text* transport, not a structured client protocol. Correction applied to [`08-blazor.md`](../architecture/08-blazor.md). |
| `LoginFlow` is close to transport-ready | [`LoginFlow.cs:304`](../../Server/Sessions/LoginFlow.cs) is the sole `StreamReader` touch | An `ILineReader` extraction (~20 LOC) unlocks the whole login state machine for any transport. |

**Rejected on the evidence:** *xterm.js as the web-client target.* Hedron's output framework is a set
of **typed message records** rendered per transport ([`TelnetOutputFormatter.cs`](../../Core/Output/TelnetOutputFormatter.cs)
pattern-matches message type and emits ANSI). Piping that through a terminal emulator serializes
structure away and asks the client to re-parse it. The correct target is a structured formatter
emitting JSON message DTOs to a component-rendered client.

**Rejected on the evidence:** *`@rendermode InteractiveAuto` / WebAssembly as a Blazor escape hatch.*
WASM runs in the browser and cannot inject `IContentDefinitionCatalog`; it would require exactly the
HTTP layer it is meant to avoid. The in-process DI design and client-side render modes are mutually
exclusive. `InteractiveServer` is the only correct Blazor mode for this host.

## The two routes

### Keep Blazor Server

**Pros.** No contract layer — a `MobTemplate` rename is a compile error in the editor. One toolchain,
one language, one CI. The two-host split-registration seam ([`08-blazor.md`](../architecture/08-blazor.md#the-two-host-model))
is well-built and untouched by any of this. The repairs that matter cost ~450 LOC across 2 slices,
and catalog caching also speeds the `generate` CLI and telnet paths — value a rewrite delivers none of.

**Cons.** Commits the player client to a circuit-per-connection model that is wrong at player scale.
Keeps ~3,900 LOC permanently outside `INV-25`. WASM/`InteractiveAuto` is unavailable (above). Lock-in
accrues at ~300 LOC per touching slice, and Phase 5 is the phase that hammers the editor hardest.

### React SPA + SignalR

**Pros.** Client-owned state makes the tune-and-observe loops structurally cheap instead of
structurally expensive (live power readout, filtered exit pickers, grid drag-to-connect). The
ecosystem answers the two worst pages directly. A JSON surface makes authoring endpoints testable in
plain xUnit via `WebApplicationFactory`, closing part of the coverage hole. It is the right
foundation for a structured (non-terminal) game client.

**Cons.** ~7 slices and ~14 architecture-review gates during a phase whose success metric is content
volume. Introduces a permanent C#/TS drift tax. Forces auth, a second toolchain, a second test tier,
and a doc migration across ~18 files. **Every `.claude/` skill is C#-shaped** — agent output on the
new tier starts *worse* than on Razor until a front-end skill exists (`INV-20`).

### Agent fluency

The project is built by a solo architect through AI agents, so model fluency in React/TS vs. Razor is
a real throughput term. Its honest form is not "React is better" but: **the performance-correct React
idiom is the default idiom, whereas the performance-correct Blazor idiom is an opt-in that was not
taken 17 slices running** (the zero-usage row above is the measurement). It is unquantifiable from
here, cuts toward React, and is explicitly *not* treated as decisive — the counterweight is the
C#-shaped skill surface, which is a measurable cost that lands on day one.

## Decision gate

**When.** At the Phase 5 → Phase 6 boundary, after the starting region is authored against the
repaired editor. Not before — the gate's whole purpose is to decide on authoring-friction evidence
that does not exist today.

**Preconditions.** Both no-regret tracks landed — `authoring-editor-repair`
**shipped** ([`../roadmap/completed/authoring-editor-repair.md`](../roadmap/completed/authoring-editor-repair.md)),
[`authoring-api-surface`](../implementation-plans/authoring-api-surface.md) still ahead — and Phase 5's
content baseline authored.

**The measurement.** A one-page bakeoff: port `MobEditor` (highest form friction, cleanest shape) to
React over the JSON surface and compare against the repaired Razor page on authoring throughput,
diff size for a field addition, and agent-iteration count. The page must render its **live power
readout** and spawn-room pickers, not stubs — that readout is one of the two tune-and-observe loops
this doc names as React's headline win, so stubbing it would remove the gate's main subject. The
bakeoff **hand-writes its one interface from the OpenAPI document**: no codegen, no Node/Vite
toolchain before the gate decides whether that toolchain is wanted at all.

**The criteria.** Go if the React page decisively beats the repaired page on real authoring work
*and* the player client is scheduled for Phase 6 *and* the world-state threading decision has an
owner. Otherwise no-go. A tie is a no-go — the burden of proof is on the migration.

### If go

Frame the migration as its own `/advise` program. High-level shape: React + TS + Vite built into the
existing host, coexisting with Blazor under separate route prefixes; editor ported **page by page,
each PR deleting its `.razor` counterpart** so there is never a half-finished rewrite; SignalR push
for job progress as the low-stakes rehearsal of the first real push path; then the player client
behind the threading decision. Adds a front-end skill under [`.claude/skills/`](../../.claude/skills/)
in the first slice (`INV-20`), and a front-end test tier to [`07-testing.md`](../architecture/07-testing.md)
(`INV-25`). Retire the three-suite table in [`08-blazor.md`](../architecture/08-blazor.md) and rewrite
that doc as the host/transport tier rather than the Blazor tier.

### If no-go

Blazor is confirmed as the editor's durable home; close this decision and record it. The player
client re-opens as a separate question at Phase 7 — a no-go here decides the *editor*, not the
client, and the two are only coupled by the "don't maintain two stacks" argument, which weakens once
the editor is good enough. Fold the remaining React-side wins back into the Blazor tier: adopt
`EditForm`/`DataAnnotationsValidator`, add `@key`/`Virtualize` where the zero-usage audit says they
belong, and consider a JS-interop island for the grid editor (`INV-19` — a scoped escape hatch, not
a stack). Update [`07-testing.md`](../architecture/07-testing.md) to state why the presentation
skip-tier remains correct once the leaked logic has moved down.

## No-regret work (valuable under both outcomes)

Two seeded slices, ~4 slices' worth of budget total, none of which prejudges the gate:

- **`authoring-editor-repair` — shipped**
  ([`../roadmap/completed/authoring-editor-repair.md`](../roadmap/completed/authoring-editor-repair.md)):
  the catalog index cache and the editor UX ratchet. A React port inherits the cache unchanged, which
  is what made it no-regret. Note the seeded framing overstated its reach — the cache speeds the
  *editor* only; the `generate` CLI and the telnet `mk*`/`set*` verbs write through
  `I*ContentWriter` directly and neither benefit from nor invalidate it.
- [`authoring-api-surface.md`](../implementation-plans/authoring-api-surface.md) — extract the leaked
  component logic into `Core/` systems (`INV-8` conformance, which also closes most of the coverage
  hole under existing rules), then a JSON transport adapter scoped to the Mob kind.

**A correction the spec gate forced, recorded because it constrains the gate itself.** The JSON
surface is *not* fully no-regret, and the first draft of this doc overstated it. Its **single**
justifying consumer is the bakeoff below — the gate's measurement cannot be built without it. The
OpenAPI document is framework-neutral and keeps its value under either outcome; **TypeScript type
generation is deliberately excluded** from that slice and deferred to the bakeoff itself, because
checking in a generated TS artifact and gating CI on it would pre-pay two costs this doc books as
React-branch cons (the C#/TS drift tax and a second CI toolchain) before the branch is chosen. Under a
no-go those costs would buy nothing. The `INV-8` extraction half is unconditionally owed and lands
either way.

## Related

- [`../architecture/08-blazor.md`](../architecture/08-blazor.md) — the current web tier as built; the three-suite end-state this decision revisits.
- [`../architecture/checklist.md`](../architecture/checklist.md) — `INV-8`, `INV-19`, `INV-20`, `INV-25`, `INV-31`.
- [`../architecture/07-testing.md`](../architecture/07-testing.md) — the presentation skip-tier this decision puts under review.
- [`../roadmap/plan.md`](../roadmap/plan.md) — where the gate sits in the phase plan.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — Web / SignalR dual client; REST / public content API; full-featured content editor; world-state threading model.
