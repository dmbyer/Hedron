using System.Linq;
using Hedron.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Hedron.Tests.Composition
{
    /// <summary>
    /// Guards the split hosted-service registration (content-tooling S0). After the
    /// <c>AddHostedService</c> calls were factored out of <see cref="CompositionRoot.Register"/>
    /// into <see cref="CompositionRoot.AddGameplayHostedServices"/>, the full gameplay host
    /// (the telnet <c>Server</c>) must still compose exactly the six hosted services it did
    /// before — the split must not silently drop one. The web authoring host composes a
    /// trimmed set, exercised by launching that host (host plumbing, not unit-tested).
    /// </summary>
    /// <remarks>
    /// Asserts on implementation-type <i>names</i> rather than <c>typeof</c> because some hosted
    /// services (e.g. <c>TelnetServer</c>) are internal to the Server assembly.
    /// </remarks>
    public sealed class HostCompositionTests
    {
        [Fact]
        public void AddGameplayHostedServices_RegistersFullGameplayHostedServiceSet()
        {
            var services = new ServiceCollection();

            services.AddGameplayHostedServices();

            var implementationTypeNames = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .Select(d => d.ImplementationType?.Name)
                .ToHashSet();

            Assert.Contains("PersistenceBootstrap", implementationTypeNames);
            Assert.Contains("WorldContentBootstrap", implementationTypeNames);
            Assert.Contains("RegistryValidationBootstrap", implementationTypeNames);
            Assert.Contains("PersistenceFlushTimer", implementationTypeNames);
            Assert.Contains("TelnetServer", implementationTypeNames);
            Assert.Contains("HeartbeatBackgroundService", implementationTypeNames);
            Assert.Equal(6, implementationTypeNames.Count);
        }
    }
}
