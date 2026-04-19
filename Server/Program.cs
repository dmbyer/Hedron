using Hedron.Core.ECS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hedron.Server;

/// <summary>
/// Composition root for the Hedron host. Phase 1 wires only the ECS primitives;
/// the telnet listener, event bus, handler registry, and command dispatcher are
/// added in Phase 2 (see <c>docs/roadmap/plan.md</c>).
/// </summary>
public static class Program
{
    public static Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // ECS world — singleton, shared between the static EcsManager bridge and
                // any DI-injected consumer. Registering the same instance on both sides
                // keeps Phase 2 free to remove the static bridge without a flag day.
                var world = new EntityService();
                EcsManager.SetWorld(world);
                services.AddSingleton(world);
            })
            .Build();

        return host.RunAsync();
    }
}
