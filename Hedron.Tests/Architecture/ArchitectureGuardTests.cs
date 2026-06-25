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
