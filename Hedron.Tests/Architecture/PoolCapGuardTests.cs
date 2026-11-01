using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hedron.Core.Modules.Attributes.Handlers;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Xunit;

namespace Hedron.Tests.Architecture
{
    /// <summary>
    /// Architecture guards for the pool-cap seam (combat-effective-scores T23, T28).
    /// <c>IStatSystem</c> delegates its pool reads to <c>IAttributeSystem</c>, so the reverse edge would
    /// be a DI cycle and an upward call; <c>IStatSystem</c> stays pure aggregation (no setters); and the
    /// reconcile handler's runtime behavior depends on one <c>Subscribe</c> line nothing else checks.
    /// </summary>
    public sealed class PoolCapGuardTests
    {
        [Fact]
        public void AttributeSystem_does_not_depend_on_IStatSystem()
        {
            var forbidden = new[] { typeof(IStatSystem), typeof(IRelativePowerSystem) };
            var type = typeof(AttributeSystem);

            var offenders = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters()).Select(p => (Name: p.Name, Type: p.ParameterType))
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(f => (Name: (string?)f.Name, Type: f.FieldType)))
                .Where(m => forbidden.Any(f => f.IsAssignableFrom(m.Type)))
                .Select(m => m.Name)
                .ToList();

            Assert.True(offenders.Count == 0,
                "AttributeSystem owns the pool cap that IStatSystem delegates to; it must not depend on IStatSystem/IRelativePowerSystem. Offenders: "
                + string.Join(", ", offenders));
        }

        [Fact]
        public void IStatSystem_declares_no_setters()
        {
            var setters = typeof(IStatSystem).GetMethods()
                .Where(m => m.ReturnType == typeof(void))
                .Select(m => m.Name)
                .ToList();

            Assert.True(setters.Count == 0,
                "IStatSystem is pure aggregation — pool and attribute writes go through IAttributeSystem. Void members: "
                + string.Join(", ", setters));
        }

        /// <summary>
        /// Source scan of the host composition: <see cref="PoolReconcileTickHandler"/> is resolved and
        /// subscribed to <c>HeartbeatTickEvent</c>. A forgotten line would compile, resolve from DI, and
        /// silently never clamp — so CI checks the wiring itself.
        /// </summary>
        [Fact]
        public void PoolReconcileTickHandler_is_subscribed_to_the_heartbeat()
        {
            var serverDir = FindServerSourceDirectory();
            Assert.True(serverDir != null, "Server/ source directory not found.");

            var source = string.Join("\n", Directory.EnumerateFiles(serverDir!, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

            var variable = Regex.Match(source, @"var\s+(\w+)\s*=\s*host\.Services\.GetRequiredService<PoolReconcileTickHandler>\(\)");
            Assert.True(variable.Success, $"{nameof(PoolReconcileTickHandler)} is never resolved in Server/.");
            Assert.Matches(new Regex($@"Subscribe<HeartbeatTickEvent>\(\s*{variable.Groups[1].Value}\s*\)"), source);
        }

        private static string? FindServerSourceDirectory()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PoolCapGuardTests).Assembly.Location)!);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Server");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Server.csproj")))
                    return candidate;
            }
            return null;
        }
    }
}
