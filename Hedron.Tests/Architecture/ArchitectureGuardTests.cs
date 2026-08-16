using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hedron.Tests.Architecture
{
    /// <summary>
    /// Tier-5 architecture guard tests. Each fact mechanically enforces one
    /// architectural invariant from docs/architecture/checklist.md and cites
    /// its INV number in the assertion failure message.
    /// </summary>
    public sealed class ArchitectureGuardTests
    {
        // ── Shared assembly reference ─────────────────────────────────────────

        // typeof(EntityService) lives in Core; its Assembly covers all Core types.
        private static readonly Assembly CoreAssembly = typeof(EntityService).Assembly;

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Select types whose namespace ends in or contains ".Systems.".</summary>
        private static IEnumerable<Type> GetSystemTypes()
        {
            return CoreAssembly.GetTypes().Where(t =>
                t.Namespace != null &&
                (t.Namespace.EndsWith(".Systems", StringComparison.Ordinal) ||
                 t.Namespace.Contains(".Systems.", StringComparison.Ordinal)));
        }

        /// <summary>Select concrete IComponent implementors in Core.</summary>
        private static IEnumerable<Type> GetComponentTypes()
        {
            return CoreAssembly.GetTypes().Where(t =>
                typeof(IComponent).IsAssignableFrom(t) &&
                t != typeof(IComponent) &&
                t.IsClass);
        }

        // ── INV-5: no-bus-in-systems ──────────────────────────────────────────

        /// <summary>
        /// INV-5: Domain and core systems never take IEventBus as a constructor parameter
        /// or hold it as a field. Only Initiators (commands, heartbeat) and Handlers may
        /// publish events.
        /// </summary>
        [Fact]
        public void Systems_do_not_depend_on_IEventBus()
        {
            var violations = new List<string>();

            foreach (var type in GetSystemTypes())
            {
                // Check constructor parameters
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        if (typeof(IEventBus).IsAssignableFrom(param.ParameterType))
                        {
                            violations.Add(
                                $"{type.FullName}: constructor parameter '{param.Name}' is IEventBus");
                        }
                    }
                }

                // Check instance fields (public or private — the rule applies to all fields)
                foreach (var field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (typeof(IEventBus).IsAssignableFrom(field.FieldType))
                    {
                        violations.Add(
                            $"{type.FullName}: field '{field.Name}' is IEventBus");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"INV-5 violated — systems must not depend on IEventBus. Violations:\n" +
                string.Join("\n", violations));
        }

        // ── INV-3: components-are-data ────────────────────────────────────────

        /// <summary>
        /// INV-3: IComponent implementors must be pure data. No user-defined public methods
        /// with domain logic, no fields or properties typed as a system or IEventBus.
        ///
        /// Allowlisted compiler-generated / object-override members:
        ///   Equals, GetHashCode, ToString, Deconstruct, &lt;Clone&gt;$,
        ///   op_Equality, op_Inequality, get_*, set_*
        ///
        /// Records generate Deconstruct, &lt;Clone&gt;$, op_Equality, op_Inequality, and
        /// property accessors — all allowlisted. The check flags only user-defined public
        /// methods beyond this set.
        /// </summary>
        [Fact]
        public void Components_are_pure_data()
        {
            // Method names that are compiler-generated or object-inherited — not domain logic.
            var allowlistedMethodNames = new HashSet<string>
            {
                "Equals",
                "GetHashCode",
                "ToString",
                "Deconstruct",
                "<Clone>$",
            };

            var violations = new List<string>();

            foreach (var type in GetComponentTypes())
            {
                // 1. Check for user-defined public methods (not accessors, not allowlisted).
                //    Only look at methods declared on this type (not inherited from object).
                foreach (var method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    // Skip property accessors (get_X / set_X) and operator overloads (op_*)
                    if (method.IsSpecialName) continue;

                    // Skip allowlisted names
                    if (allowlistedMethodNames.Contains(method.Name)) continue;

                    violations.Add(
                        $"{type.FullName}: has user-defined public method '{method.Name}' — " +
                        $"components must be pure data (INV-3)");
                }

                // 2. Check for fields or properties typed as a system or IEventBus.
                foreach (var field in type.GetFields(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (IsSystemOrBusType(field.FieldType))
                    {
                        violations.Add(
                            $"{type.FullName}: field '{field.Name}' has system/bus type '{field.FieldType.Name}' " +
                            $"(INV-3)");
                    }
                }

                foreach (var prop in type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (IsSystemOrBusType(prop.PropertyType))
                    {
                        violations.Add(
                            $"{type.FullName}: property '{prop.Name}' has system/bus type '{prop.PropertyType.Name}' " +
                            $"(INV-3)");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"INV-3 violated — components must be pure data. Violations:\n" +
                string.Join("\n", violations));
        }

        /// <summary>
        /// Returns true if the given type is a system interface/class or IEventBus — types
        /// that must not appear on component fields (INV-3).
        /// </summary>
        private static bool IsSystemOrBusType(Type t)
        {
            // Unwrap arrays, List<T>, Dictionary<K,V>, etc.
            if (t.IsArray) return IsSystemOrBusType(t.GetElementType()!);
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    if (IsSystemOrBusType(arg)) return true;
                return false;
            }

            if (typeof(IEventBus).IsAssignableFrom(t)) return true;

            // System interfaces end in "System" and live in a Systems namespace
            if (t.IsInterface && t.Name.EndsWith("System", StringComparison.Ordinal) &&
                t.Namespace != null &&
                (t.Namespace.EndsWith(".Systems", StringComparison.Ordinal) ||
                 t.Namespace.Contains(".Systems.", StringComparison.Ordinal)))
                return true;

            // Concrete system classes (non-interface) in a Systems namespace
            if (!t.IsInterface && !t.IsValueType && t.Namespace != null &&
                (t.Namespace.EndsWith(".Systems", StringComparison.Ordinal) ||
                 t.Namespace.Contains(".Systems.", StringComparison.Ordinal)))
                return true;

            return false;
        }

        // ── INV-13: entity-refs-are-uint ──────────────────────────────────────

        /// <summary>
        /// INV-13: Components must not hold cross-entity references as IComponent instances
        /// or as the Entity value type. Entity references must be stored as uint.
        ///
        /// Heuristic: flag any public field or property whose declared type is:
        ///   (a) an IComponent implementor, or
        ///   (b) the Entity record struct (Hedron.Core.ECS.Entity).
        /// Collections of the above are also flagged (List&lt;T&gt;, T[]).
        /// </summary>
        [Fact]
        public void Component_entity_references_are_uint()
        {
            var entityType = typeof(Entity);
            var violations = new List<string>();

            foreach (var type in GetComponentTypes())
            {
                // Check public properties (the typical data surface of a component)
                foreach (var prop in type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public))
                {
                    if (prop.DeclaringType == typeof(object)) continue;
                    if (ReferencesEntityOrComponent(prop.PropertyType))
                    {
                        violations.Add(
                            $"{type.FullName}: property '{prop.Name}' of type '{prop.PropertyType.Name}' " +
                            $"holds an IComponent or Entity reference — use uint instead (INV-13)");
                    }
                }

                // Check public fields
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.DeclaringType == typeof(object)) continue;
                    if (ReferencesEntityOrComponent(field.FieldType))
                    {
                        violations.Add(
                            $"{type.FullName}: field '{field.Name}' of type '{field.FieldType.Name}' " +
                            $"holds an IComponent or Entity reference — use uint instead (INV-13)");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"INV-13 violated — entity references in components must be uint, not IComponent or Entity. " +
                $"Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// Returns true if the given type is, or contains, an IComponent implementation
        /// or the Entity struct.
        /// </summary>
        private static bool ReferencesEntityOrComponent(Type t)
        {
            if (t == typeof(Entity)) return true;
            if (t != typeof(IComponent) && typeof(IComponent).IsAssignableFrom(t) && t.IsClass)
                return true;

            if (t.IsArray) return ReferencesEntityOrComponent(t.GetElementType()!);
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    if (ReferencesEntityOrComponent(arg)) return true;
            }

            return false;
        }

        // ── INV-23: world-content-not-persistent ──────────────────────────────

        /// <summary>
        /// INV-23: World-content component types (RoomComponent, AreaComponent, ProtectionComponent)
        /// must not carry the [Persistent] attribute. World content is always fresh-spawned from
        /// YAML/templates — never written to SQLite.
        ///
        /// ProtectionComponent is mob world-content (durable form is MobTemplate YAML).
        /// Its non-persistence is independently proven by the Tier-4 YAML round-trip test
        /// in MobProtectionRoundTripTests (the CurrencyLootComponent precedent).
        /// </summary>
        [Fact]
        public void World_content_components_are_not_persistent()
        {
            var roomComponent = typeof(Hedron.Core.ECS.Components.RoomComponent);
            var areaComponent = typeof(Hedron.Core.ECS.Components.AreaComponent);
            var protectionComponent = typeof(Hedron.Core.ECS.Components.ProtectionComponent);

            var violations = new List<string>();

            if (roomComponent.GetCustomAttribute<PersistentAttribute>() != null)
                violations.Add($"{roomComponent.FullName} carries [Persistent]");

            if (areaComponent.GetCustomAttribute<PersistentAttribute>() != null)
                violations.Add($"{areaComponent.FullName} carries [Persistent]");

            if (protectionComponent.GetCustomAttribute<PersistentAttribute>() != null)
                violations.Add($"{protectionComponent.FullName} carries [Persistent]");

            Assert.True(
                violations.Count == 0,
                $"INV-23 violated — world-content components (RoomComponent, AreaComponent, ProtectionComponent) " +
                $"must not be [Persistent]. Violations:\n" +
                string.Join("\n", violations));
        }

        // ── INV-26: no ambient randomness + wall-clock ────────────────────────

        /// <summary>
        /// INV-26: System files must not use ambient randomness (Random.Shared, new Random())
        /// or direct wall-clock reads (DateTime.UtcNow/Now, DateTimeOffset.UtcNow/Now, .Today).
        /// All such access must go through injected seams (IRandom, IClock).
        ///
        /// Source-scans every *.cs under any Systems/ subdirectory of the Core project.
        /// The Core source root is resolved by walking up from the test-assembly binary location
        /// until a directory named "Core" is found.
        /// </summary>
        [Fact]
        public void Systems_do_not_use_ambient_randomness_or_wall_clock()
        {
            var coreDir = FindCoreSourceDirectory();
            Assert.True(
                coreDir != null,
                "INV-26 pre-check: could not locate the Core/ source directory relative to " +
                "the test assembly. Walk-up search failed.");

            // The seam adapters (SystemClock, SystemRandom) and their interface definitions
            // (IClock, IRandom) are explicitly allowed to reference DateTime.UtcNow /
            // Random.Shared — they ARE the seam. Exclude them from the guard so the test
            // focuses on callers that should use the seam but don't.
            var seamAdapterFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SystemClock.cs",
                "SystemRandom.cs",
                // SeededRandom IS an IRandom seam adapter — the one place a deterministic
                // per-run generator instance is constructed from a seed (INV-26). Like
                // SystemRandom, it is the seam, not a caller that should consume the seam.
                "SeededRandom.cs",
                "IClock.cs",
                "IRandom.cs",
            };

            var systemsFiles = Directory.EnumerateFiles(coreDir!, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    // Exclude the seam adapter / interface files themselves
                    if (seamAdapterFileNames.Contains(Path.GetFileName(f))) return false;

                    // Include files whose path contains a \Systems\ or /Systems/ segment
                    var rel = f.Substring(coreDir!.Length);
                    return rel.Contains(Path.DirectorySeparatorChar + "Systems" + Path.DirectorySeparatorChar) ||
                           rel.Contains(Path.AltDirectorySeparatorChar + "Systems" + Path.AltDirectorySeparatorChar) ||
                           // Also catch files directly in a "Systems" directory
                           Path.GetDirectoryName(f)!.EndsWith("Systems", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            Assert.True(
                systemsFiles.Count > 0,
                "INV-26 pre-check: no *.cs files found under any Systems/ folder in Core/. " +
                $"Searched: {coreDir}");

            // Patterns that are forbidden in system files
            var forbiddenPatterns = new[]
            {
                ("Random.Shared",      "use IRandom (INV-26)"),
                ("new Random(",        "use IRandom (INV-26)"),
                ("DateTime.UtcNow",    "use IClock (INV-26)"),
                ("DateTime.Now",       "use IClock (INV-26)"),
                ("DateTimeOffset.UtcNow", "use IClock (INV-26)"),
                ("DateTimeOffset.Now", "use IClock (INV-26)"),
                (".Today",             "use IClock (INV-26)"),
            };

            var violations = new List<string>();

            foreach (var file in systemsFiles)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    foreach (var (pattern, reason) in forbiddenPatterns)
                    {
                        if (line.Contains(pattern, StringComparison.Ordinal))
                        {
                            violations.Add(
                                $"{Path.GetRelativePath(coreDir!, file)}:{i + 1}: " +
                                $"contains '{pattern}' — {reason}");
                        }
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"INV-26 violated — systems must not use ambient randomness or direct wall-clock reads. " +
                $"Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// Walks up from the test-assembly binary directory until it finds a directory
        /// that contains a "Core" subdirectory with a "Core.csproj" file, then returns
        /// that "Core" path. Returns null if not found after 8 levels.
        /// </summary>
        private static string? FindCoreSourceDirectory()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(ArchitectureGuardTests).Assembly.Location)!);

            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Core");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "Core.csproj")))
                {
                    return candidate;
                }
            }

            return null;
        }

        // ── Authoring editors: blueprint-id edits must preserve the form ──────

        /// <summary>
        /// A content editor's <c>OnBlueprintIdChanged</c> must rekey the in-progress definition via
        /// <c>IContentDefinitionCatalog.WithBlueprintId</c>, never re-mint it with <c>CreateNew</c>
        /// — the latter silently discards every other field the author has filled in. The defect
        /// survived review in four separate <c>.razor</c> files, so it is guarded rather than trusted.
        /// </summary>
        [Fact]
        public void Editors_do_not_recreate_the_definition_when_the_blueprint_id_changes()
        {
            var webDir = FindWebSourceDirectory();
            Assert.True(
                webDir != null,
                "pre-check: could not locate the Hedron.Web/ source directory relative to the test assembly.");

            var razorFiles = Directory.EnumerateFiles(webDir!, "*.razor", SearchOption.AllDirectories).ToList();
            Assert.True(razorFiles.Count > 0, $"pre-check: no *.razor files found under {webDir}");

            var violations = new List<string>();
            var handlersScanned = 0;
            foreach (var file in razorFiles)
            {
                var lines = File.ReadAllLines(file);
                var handlerLine = Array.FindIndex(
                    lines, l => l.Contains("void OnBlueprintIdChanged", StringComparison.Ordinal));
                if (handlerLine < 0)
                    continue;

                handlersScanned++;

                // Scan the handler body (to its closing brace at the same indent, or 12 lines).
                for (var i = handlerLine; i < Math.Min(lines.Length, handlerLine + 12); i++)
                {
                    if (i > handlerLine && lines[i].TrimEnd() == "    }")
                        break;
                    if (lines[i].Contains("Catalog.CreateNew", StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(webDir!, file)}:{i + 1} — OnBlueprintIdChanged calls " +
                            "Catalog.CreateNew; use Catalog.WithBlueprintId so the in-progress form survives.");
                    }
                }
            }

            // Guard the guard: if the handler is ever renamed, this test must fail loudly rather
            // than silently scanning nothing.
            Assert.True(
                handlersScanned >= 4,
                $"pre-check: expected an OnBlueprintIdChanged handler in each of the four content " +
                $"editors; found {handlersScanned}.");

            Assert.True(
                violations.Count == 0,
                "Changing the blueprint id on a New form must preserve every other authored field. " +
                "Violations:\n" + string.Join("\n", violations));
        }

        // ── INV-8: the web host holds no leaked decision logic ────────────────────

        /// <summary>
        /// INV-8/INV-15: two specific constructs that were extracted out of `.razor` components by
        /// authoring-api-surface WP1 must not reappear in <c>Hedron.Web/</c>.
        ///
        /// <list type="bullet">
        ///   <item><c>DirectionExtensions.FromOffset</c> — inverting a cell offset back to a
        ///     direction is the observable half of the grid's <em>connect policy</em>, which now
        ///     lives in <c>IAreaLayoutSystem.ConnectAsync</c>. Deliberately not a blanket "no
        ///     <c>Direction</c> in a razor": the grid legitimately renders per-direction UI and
        ///     calls <c>DisconnectEdge(roomId, Direction)</c>, and <c>Direction.Offset()</c> (the
        ///     forward mapping, used only to decide which exits render as edge tabs) is
        ///     presentation.</item>
        ///   <item><c>new PowerBudgetSystem(</c> — constructing a DI-registered type inside a
        ///     component. The pure preview path is <c>PowerBudgetMath</c>.</item>
        /// </list>
        ///
        /// This extends the guard tier's existing <c>Hedron.Web/</c> source scan (see
        /// <see cref="Editors_do_not_recreate_the_definition_when_the_blueprint_id_changes"/>, which
        /// already scans <c>.razor</c> via the same <c>FindWebSourceDirectory</c>) to <c>.cs</c> as
        /// well as <c>.razor</c>.
        /// </summary>
        [Fact]
        public void Web_host_does_not_hold_extracted_authoring_logic()
        {
            var webDir = FindWebSourceDirectory();
            Assert.True(
                webDir != null,
                "pre-check: could not locate the Hedron.Web/ source directory relative to the test assembly.");

            var sourceFiles = Directory
                .EnumerateFiles(webDir!, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Where(f => !IsUnderBuildOutput(webDir!, f))
                .ToList();

            Assert.True(sourceFiles.Count > 0, $"pre-check: no source files found under {webDir}");

            var forbidden = new[]
            {
                ("DirectionExtensions.FromOffset",
                 "the grid connect policy lives in IAreaLayoutSystem.ConnectAsync (INV-8)"),
                ("new PowerBudgetSystem(",
                 "preview derived ranges via the pure PowerBudgetMath, never a component-constructed oracle (INV-8)"),
            };

            var violations = new List<string>();
            foreach (var file in sourceFiles)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (var (pattern, reason) in forbidden)
                    {
                        if (lines[i].Contains(pattern, StringComparison.Ordinal))
                        {
                            violations.Add(
                                $"{Path.GetRelativePath(webDir!, file)}:{i + 1}: contains '{pattern}' — {reason}");
                        }
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "INV-8 violated — decision logic extracted from the web host must not return. " +
                "Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>
        /// The authoring API's cross-origin protection is that a cross-origin JSON <c>fetch</c>
        /// triggers a preflight, and that preflight fails <em>only because no CORS policy exists on
        /// this host</em>. Registering one — an <c>AllowAnyOrigin</c> above all — would silently
        /// undo the mitigation without touching a line of endpoint code. So the absence of CORS is
        /// load-bearing and is guarded rather than trusted (see <c>Hedron.Web/Api/LocalOriginFilter</c>).
        ///
        /// The runtime half of this check — that no CORS service resolves out of the built host —
        /// lives in <c>AuthoringApiTests</c>, and catches a policy arriving transitively.
        /// </summary>
        [Fact]
        public void Web_host_registers_no_CORS_policy()
        {
            var webDir = FindWebSourceDirectory();
            Assert.True(webDir != null, "pre-check: could not locate the Hedron.Web/ source directory.");

            var sourceFiles = Directory
                .EnumerateFiles(webDir!, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsUnderBuildOutput(webDir!, f))
                .ToList();

            Assert.True(sourceFiles.Count > 0, $"pre-check: no *.cs files found under {webDir}");

            var violations = new List<string>();
            foreach (var file in sourceFiles)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("AddCors(", StringComparison.Ordinal) ||
                        lines[i].Contains("UseCors(", StringComparison.Ordinal) ||
                        lines[i].Contains("RequireCors(", StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetRelativePath(webDir!, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "The authoring host must register no CORS policy — the API's cross-origin protection " +
                "depends on preflight failing. Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>True for a path under the project's <c>bin/</c> or <c>obj/</c> output.</summary>
        private static bool IsUnderBuildOutput(string root, string file)
        {
            var relative = Path.GetRelativePath(root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Any(s =>
                s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("obj", StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindWebSourceDirectory()
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(typeof(ArchitectureGuardTests).Assembly.Location)!);

            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Hedron.Web");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "Hedron.Web.csproj")))
                {
                    return candidate;
                }
            }

            return null;
        }

        // ── INV-2: power-budget-oracle-is-core-tier ───────────────────────────

        /// <summary>
        /// INV-2: <c>PowerBudgetSystem</c> (Core/Systems/) must stay core-tier-generic — it must
        /// import no <c>Core/Modules/&lt;Feature&gt;/</c> domain type at all (not just the
        /// <c>Account</c> module's <c>CharacterDefaultsOptions</c>; the reference base build AND
        /// the tier-band constants are co-located mirrors, never a reference into the domain
        /// <c>Account</c> or <c>Ascension</c> modules). <c>Hedron.Core.Modules.Stats</c> is
        /// allowlisted — it holds only the <c>ScoreId</c> enum, a shared vocabulary key with no
        /// business logic, the same role <c>Entity</c>/component-type ids play elsewhere. Structurally,
        /// the system takes exactly one constructor dependency — the plain-data
        /// <see cref="Hedron.Core.Systems.PowerBudgetTunables"/> record (see
        /// <c>docs/design/power-model.md</c>: "never gains a *service or domain* dependency — the
        /// single caller-composed plain-data tunables record is the one permitted constructor
        /// input"). Every other input is the caller-supplied <c>PowerSnapshot</c>.
        /// </summary>
        [Fact]
        public void PowerBudgetSystem_has_no_domain_module_dependency()
        {
            var type = typeof(Hedron.Core.Systems.PowerBudgetSystem);

            var ctor = Assert.Single(type.GetConstructors());
            var parameters = ctor.GetParameters();
            Assert.True(
                parameters.Length == 1 && parameters[0].ParameterType == typeof(Hedron.Core.Systems.PowerBudgetTunables),
                "INV-2 violated — PowerBudgetSystem must take exactly one constructor dependency, " +
                "of type PowerBudgetTunables (every other input is a caller-supplied PowerSnapshot).");

            var coreDir = FindCoreSourceDirectory();
            Assert.True(coreDir != null, "INV-2 pre-check: could not locate the Core/ source directory.");

            var files = new[]
            {
                Path.Combine(coreDir!, "Systems", "PowerBudgetSystem.cs"),
                Path.Combine(coreDir!, "Systems", "PowerBudgetMath.cs"),
                Path.Combine(coreDir!, "Systems", "PowerBudgetTunables.cs"),
                Path.Combine(coreDir!, "Systems", "IPowerBudgetSystem.cs"),
                Path.Combine(coreDir!, "Systems", "PowerSnapshot.cs"),
                Path.Combine(coreDir!, "Systems", "PowerBand.cs"),
                Path.Combine(coreDir!, "Systems", "PowerRange.cs"),
            };

            // The only Core/Modules/<Feature>/ namespace the oracle may import — ScoreId is a
            // plain-data vocabulary key, not a domain system or options type.
            const string allowlistedModuleNamespace = "using Hedron.Core.Modules.Stats;";

            var violations = new List<string>();
            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                foreach (var line in File.ReadAllLines(file))
                {
                    var trimmed = line.TrimStart();

                    if (!trimmed.StartsWith("using Hedron.Core.Modules.", StringComparison.Ordinal))
                        continue;

                    if (trimmed == allowlistedModuleNamespace)
                        continue;

                    violations.Add($"{Path.GetFileName(file)}: '{line.Trim()}'");
                }
            }

            Assert.True(
                violations.Count == 0,
                "INV-2 violated — the power-budget oracle must not import any Core/Modules/<Feature>/ " +
                "domain type other than Hedron.Core.Modules.Stats (ScoreId). Violations:\n" +
                string.Join("\n", violations));
        }

        // ── sim-5: conformance fitter ctor shape stays pinned to its five named seams ──

        /// <summary>
        /// Sim-5 spec-gate finding: pins <c>TemplateConformanceSystem</c>'s constructor to exactly
        /// its five named seams (<c>IContentDefinitionCatalog</c>, <c>IPowerBudgetSystem</c>,
        /// <c>IItemPowerProjectionSystem</c>, <c>IMobPowerProjectionSystem</c>,
        /// <c>IBalanceAuditSystem</c>) — precedent <see cref="PowerBudgetSystem_has_no_domain_module_dependency"/>.
        /// No existing guard would catch the fitter quietly gaining an <c>EntityService</c> or
        /// <c>IPersistenceSystem</c> dependency, which would be scope creep into live-entity
        /// mutation (this slice is YAML-side only, INV-22/23).
        /// </summary>
        [Fact]
        public void TemplateConformanceSystem_has_exactly_the_five_named_seam_dependencies()
        {
            var type = typeof(Hedron.Core.Modules.BalanceInspection.Systems.TemplateConformanceSystem);

            var ctor = Assert.Single(type.GetConstructors());
            var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

            var expected = new[]
            {
                typeof(Hedron.Core.Modules.Authoring.Systems.IContentDefinitionCatalog),
                typeof(Hedron.Core.Systems.IPowerBudgetSystem),
                typeof(Hedron.Core.Modules.Items.Systems.IItemPowerProjectionSystem),
                typeof(Hedron.Core.Modules.Mobs.Systems.IMobPowerProjectionSystem),
                typeof(Hedron.Core.Modules.BalanceInspection.Systems.IBalanceAuditSystem),
            };

            Assert.True(
                expected.Length == parameterTypes.Length && expected.All(parameterTypes.Contains),
                "Sim-5 finding violated — TemplateConformanceSystem's constructor must take exactly " +
                $"{expected.Length} parameters, one per named seam. Actual: " +
                string.Join(", ", parameterTypes.Select(t => t.Name)));
        }

        // ── sim-2: simulation engine touches neither the event bus nor the live world ──

        /// <summary>
        /// Sim-2 Postcondition 1/4: the simulation engine publishes nothing and never resolves the
        /// host's live world. No type in <c>Core/Modules/Simulation/</c> may reference
        /// <see cref="IEventBus"/> (constructor parameter or field) or reference
        /// <c>Hedron.Core.ECS.EcsManager</c> anywhere in its source (a static class, so a source
        /// scan — reflection can't enumerate arbitrary static-member reads).
        /// </summary>
        [Fact]
        public void Simulation_module_does_not_reference_EventBus_or_EcsManager()
        {
            var violations = new List<string>();

            foreach (var type in CoreAssembly.GetTypes().Where(t =>
                t.Namespace != null && t.Namespace.StartsWith("Hedron.Core.Modules.Simulation", StringComparison.Ordinal)))
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        if (typeof(IEventBus).IsAssignableFrom(param.ParameterType))
                            violations.Add($"{type.FullName}: constructor parameter '{param.Name}' is IEventBus");
                    }
                }

                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (typeof(IEventBus).IsAssignableFrom(field.FieldType))
                        violations.Add($"{type.FullName}: field '{field.Name}' is IEventBus");
                }
            }

            var coreDir = FindCoreSourceDirectory();
            Assert.True(coreDir != null, "sim-2 pre-check: could not locate the Core/ source directory.");

            var simulationDir = Path.Combine(coreDir!, "Modules", "Simulation");
            Assert.True(Directory.Exists(simulationDir), "sim-2 pre-check: Core/Modules/Simulation/ not found.");

            foreach (var file in Directory.EnumerateFiles(simulationDir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.Contains("EcsManager", StringComparison.Ordinal))
                        violations.Add($"{Path.GetRelativePath(coreDir!, file)}: references EcsManager");
                }
            }

            Assert.True(
                violations.Count == 0,
                "Sim-2 Postcondition 1/4 violated — the simulation engine must not reference IEventBus " +
                "or EcsManager. Violations:\n" + string.Join("\n", violations));
        }

        // ── DI smoke test ─────────────────────────────────────────────────────

        /// <summary>
        /// INV-DI: The composition root must build a valid ServiceProvider, and every
        /// ICommand, every IEventHandler&lt;&gt;, and every registered system type must
        /// resolve without throwing.
        ///
        /// Supplies an in-memory IConfiguration with all keys the composition root reads,
        /// mirroring Server/appsettings.json. Hosted services (IHostedService) are not
        /// resolved — they require a real hosting environment.
        /// </summary>
        [Fact]
        public void CompositionRoot_resolves_all_commands_handlers_and_systems()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Output
                    ["Output:DefaultColor"]                    = "true",
                    // Persistence
                    ["Persistence:FlushIntervalSeconds"]       = "60",
                    ["Persistence:DatabasePath"]               = ":memory:",
                    // World
                    ["World:ContentDirectory"]                 = "data/content/",
                    ["World:StartingRoomBlueprintId"]          = "room.crossroads",
                    // Admin
                    ["Admin:PrivilegedNames:0"]                = "admin",
                    // Heartbeat
                    ["Heartbeat:IntervalMs"]                   = "2000",
                    // Server
                    ["Server:Port"]                            = "4000",
                    // CharacterDefaults
                    ["CharacterDefaults:StartingAbilities:0"]  = "kick",
                    ["CharacterDefaults:StartingAbilities:1"]  = "empower",
                    ["CharacterDefaults:AttributeDefault"]     = "10",
                    ["CharacterDefaults:MaxHp"]                = "100",
                    ["CharacterDefaults:MaxMana"]              = "50",
                    ["CharacterDefaults:MaxStamina"]           = "50",
                    ["CharacterDefaults:MaxAstra"]             = "10",
                    // Death
                    ["Death:HpFloor"]                          = "-10",
                    ["Death:BleedPerTick"]                     = "1",
                    ["Death:RespawnPoolPercent"]               = "0.25",
                    // Logging
                    ["Logging:LogLevel:Default"]               = "Warning",
                })
                .Build();

            var services = new ServiceCollection();

            // IConfiguration must be in the container — some hosted services still inject it
            // directly (TelnetServer for Server:Port, HeartbeatBackgroundService for Heartbeat:IntervalMs).
            services.AddSingleton<IConfiguration>(config);

            // Logging is required by many systems registered in the composition root.
            services.AddLogging();

            var exception = Record.Exception(() => services.Register(config));
            Assert.True(
                exception == null,
                $"INV-DI violated — CompositionRoot.Register threw during service registration: " +
                $"{exception}");

            ServiceProvider provider;
            var buildException = Record.Exception(() => provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = false }));
            Assert.True(
                buildException == null,
                $"INV-DI violated — BuildServiceProvider threw: {buildException}");

            provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = false });

            var violations = new List<string>();

            // Resolve all ICommand registrations
            var commandException = Record.Exception(() =>
                provider.GetServices<Hedron.Core.Commands.ICommand>().ToList());
            if (commandException != null)
                violations.Add($"ICommand resolution threw: {commandException.Message}");

            // Resolve all system interfaces that are registered.
            // Detect these by looking at every descriptor whose service type is an interface
            // living in a Systems namespace.
            var systemDescriptors = services
                .Where(d =>
                    d.ServiceType.IsInterface &&
                    d.ServiceType.Namespace != null &&
                    (d.ServiceType.Namespace.EndsWith(".Systems", StringComparison.Ordinal) ||
                     d.ServiceType.Namespace.Contains(".Systems.", StringComparison.Ordinal)))
                .ToList();

            foreach (var descriptor in systemDescriptors)
            {
                var ex = Record.Exception(() => provider.GetRequiredService(descriptor.ServiceType));
                if (ex != null)
                    violations.Add(
                        $"System {descriptor.ServiceType.Name} resolution threw: {ex.Message}");
            }

            // Resolve known handler singletons by their concrete type.
            // Handlers are registered as concrete types (not under a shared interface).
            // Filter: concrete class, is accessible (public), implements IEventHandler<>.
            // Exclude IHostedService registrations — those require a full hosting environment.
            var genericHandlerDef = typeof(IEventHandler<>);
            var hostedServiceType = typeof(Microsoft.Extensions.Hosting.IHostedService);

            var handlerDescriptors = services
                .Where(d =>
                    !d.ServiceType.IsInterface &&
                    d.ServiceType.IsClass &&
                    d.ServiceType.IsPublic &&
                    !hostedServiceType.IsAssignableFrom(d.ServiceType) &&
                    d.ServiceType.GetInterfaces().Any(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition() == genericHandlerDef))
                .ToList();

            foreach (var descriptor in handlerDescriptors)
            {
                var ex = Record.Exception(() => provider.GetRequiredService(descriptor.ServiceType));
                if (ex != null)
                    violations.Add(
                        $"Handler {descriptor.ServiceType.Name} resolution threw: {ex.Message}");
            }

            Assert.True(
                violations.Count == 0,
                $"INV-DI violated — service resolution failures:\n" +
                string.Join("\n", violations));
        }
    }
}
