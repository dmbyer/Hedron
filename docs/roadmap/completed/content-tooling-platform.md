# Completed — Content-Tooling Platform

**Spec:** [`../../implementation-plans/content-tooling-platform.md`](../../implementation-plans/content-tooling-platform.md) (architecture-advisor brief) · [`../../implementation-plans/bulk-content-generation.md`](../../implementation-plans/bulk-content-generation.md) (T1) · [`../../implementation-plans/content-authoring-editor.md`](../../implementation-plans/content-authoring-editor.md) (T2)
**Commits:** `c2d6d77` (WP-1) · `c4dc84f` (S0) · `3088684` (T1) · `d472375` (T2-WP-2) · `92f6208` (T2-WP-3)
**Advisor-initiated** — framed with `/advise`, planned with `implementation-planner`, both spec-gated and code-gated; not part of the numbered slice queue.

## Outcome

Moved content authoring from telnet-only admin commands toward UI- and headless-first tooling, without forking authoring logic. A shared **content-definition layer** (read/list/load/create/validate/write over the YAML area/room/item/mob families) was factored out of the existing builders/writers/boot validator so every surface — telnet, a new Blazor editor, and a new bulk generator — is a thin caller of the same systems. Two consumer surfaces shipped: a **headless `generate` run-mode** that emits reproducible, validated YAML worlds for scaling tests, and an **in-process Blazor Server authoring editor** (`Hedron.Web`) for browsing/creating/editing all four content kinds. All authoring stays off the live world (YAML + reload), so heartbeat-concurrency remains deferred.

## Shipped pieces

| Surface | Location | Notes |
|---|---|---|
| `IContentDefinitionCatalog` / `ContentDefinitionCatalog` | `Core/Modules/Authoring/Systems/` | List/Load/SaveAsync/CreateNew over all four `ContentKind`s; writes YAML only (INV-12/22/23); validates before write |
| `ContentKind`, `ContentDefinition`, `ContentSummary`, `ContentWriteResult`, `AdhocBlueprintId` | `Core/Modules/Authoring/` | DTOs + shared ad-hoc blueprint-id helper |
| `IContentValidator` / `ContentValidator` / `ValidationReport` | `Core/Modules/World/Systems/` | Two call modes — `ValidateRegistry` (boot sweep) + `Validate(IEntityTemplate)` (single in-memory definition); returns structured report, never throws |
| `RegistryValidationBootstrap` (refactored) | `Server/` | Now delegates rules to `IContentValidator`; owns only host fail-fast policy |
| `AuthoringModule` (`AddAuthoringModule`) | `Core/Modules/Authoring/` | Registers the catalog + generation system |
| `IContentGenerationSystem` / `ContentGenerationSystem` | `Core/Modules/Authoring/Systems/` | Composes the four content writers from a `GenerationProfile`; connected room graphs; returns `GenerationResult`, never publishes (INV-5) |
| `GenerationProfile`, `GenerationResult`, `AspectMixEntry`, `ScalingCurve` | `Core/Modules/Authoring/` | Generation profile data + result |
| `SeededRandom : IRandom` | `Core/Systems/` | Seeded determinism seam (INV-26); architecture-guard seam-adapter allowlist extended |
| `generate` run-mode | `Server/GenerationRunMode.cs`, `Server/Program.cs` (`Main` → `Task<int>`) | No-chain Initiator (INV-10); `--profile <path> [--seed N]`; validates emitted defs in-memory; exit-code status |
| Split hosted-service registration | `Server/CompositionRoot.cs` | `Register` = pure DI; `AddGameplayHostedServices` (telnet host) + `AddContentBootstrapHostedServices` (web host) compose per-host |
| `Hedron.Web` Blazor Server host | `Hedron.Web/` (new `Microsoft.NET.Sdk.Web` project) | Loopback-only; boots engine via `CompositionRoot.Register` + bootstraps-only set; browser + area/room/item/mob editors + apply-via-reload |
| Sample generation profile | `content/profiles/example.yaml` | Documented profile shape |
| Run/config docs | `README.md` (rewritten); `docs/architecture/flows/` Flow 29 (bulk-gen) + Flow 30 (offline edit); `docs/reference/systems.md`; `docs/architecture/00-overview.md` (two-host note) | |

## Tests shipped

`dotnet test` green at **619** (was 587 at area-model; +32 across the platform).

- `Hedron.Tests/World/ContentValidatorTests.cs` — both validator call modes (registry sweep returns structured errors without throwing; single-definition area aspect-composition checks).
- `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs` — List/Load/SaveAsync/CreateNew round-trips for **all four** kinds (area/room added in WP-1; item/mob added in WP-3, covering the `ItemContentWriter`/`MobContentWriter` paths); validation-blocks-write; `CreateNew` makes no live entity (INV-12).
- `Hedron.Tests/Registry/RegistryValidationTests.cs` — helper retargeted onto `ContentValidator`+bootstrap; the 15 existing fail-fast cases preserved (bootstrap still throws on invalid content).
- `Hedron.Tests/Authoring/ContentGenerationSystemTests.cs` + `GenerationRunModeTests.cs` — determinism under fixed seed, no ambient nondeterminism, count fidelity, connected-graph reachability, emitted-YAML round-trip, profile round-trip, non-zero-exit paths.
- `Hedron.Tests/Composition/HostCompositionTests.cs` — guards that `AddGameplayHostedServices` still registers all six gameplay services and `AddContentBootstrapHostedServices` registers only the two bootstraps (no telnet/heartbeat/persistence).

Blazor components/pages are presentation skip-tier per the testing rubric (all logic lives in the tested catalog/validator).

## Spec-review provenance

Both tracks passed spec-review and code-review gates.

- **Spec gate (T1):** SR-5 — a dangling "Open question 2" on registry hydration was load-bearing (the boot validator scans live `AreaComponent` entities, which would contradict the no-entities guarantee). Resolved as **Resolved Decision 4**: the `generate` run-mode hydrates definition registries only and validates via the single-definition (in-memory) validator mode — never spawning world content. INV-12/23 provably hold.
- **Spec gate (T2):** INV-20 — the split registration + second host made the `add-core-system`/`add-domain-system` "register in `Server/Program.cs`" guidance stale; both skills updated.
- **Code gate (WP-1):** INV-20 stale validation note in `add-core-system` (pointed new families at the bootstrap instead of `IContentValidator`) — fixed.
- **Code gate (T1):** confirmed the `SeededRandom` architecture-guard allowlist extension is a legitimate seam adapter (like `SystemRandom`), not a guard weakening. Cosmetic `IRegistryValidator`→`IContentValidator` doc-name drift reconciled.
- **Code gates (T2-WP-2, T2-WP-3):** clean, no findings.

## Notable design points

- **The shared backing is the C# system layer, not an HTTP API.** Telnet commands, the Blazor editor, and the bulk generator are all thin callers of the same definition/generation systems — surface parity is free as long as no authoring logic leaks into a command, a Blazor component, or the generation trigger.
- **Authoring is file-only / off the tick.** Edits write YAML; the live world is touched only via the existing `reload` path. This deliberately defers all live-world concurrency.
- **Split hosted-service registration** is the seam that scales one process from "authoring-only" to the eventual unified three-suite web surface (authoring + player client + live admin) without reshaping `CompositionRoot.Register`.
- **Deferred (recorded in `backlog.md`):** live / instant-preview editing (needs heartbeat-loop marshaling); the player web-client + live-admin suites (need the live world + web auth + the deferred SignalR `ISession` unification); a REST/public content API; migrating the four `mk*` builders onto the shared `AdhocBlueprintId`.

## Deviations from the use-case docs

- The shared validator shipped as **`IContentValidator`** (the platform brief and an early planner draft floated `IRegistryValidator`); docs reconciled.
- **S0 (split hosted-service registration)** was pulled out of T2-WP-2 into its own commit so it could land before both tracks (it is shared infrastructure), rather than living inside WP-2 as originally specced.
- Flow numbering: T1 took **Flow 29**, so the T2 offline-edit flow is **Flow 30** (the T2 spec originally also said 29; corrected).

## Follow-ups unlocked

- **Content volume now cheap:** `generate` produces reproducible, scaled, walkable test worlds on demand — the substrate for combat/scaling-balance testing.
- **The Blazor host is the foundation** the deferred player-client and live-admin web suites inherit (same process, same engine DI, `ISession`/SignalR seam pre-shaped).
- The shopping/crafting and later content-heavy slices can author via the editor or bulk-seed via profiles instead of hand-rolling YAML.
