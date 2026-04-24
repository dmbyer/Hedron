using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hedron.Server;

/// <summary>
/// Composition root for the Hedron host. Phase 2 is wiring up the foundation a step at
/// a time — ECS world and event bus so far; the handler/system contracts, command
/// dispatcher, telnet listener, and MVP modules come in later steps (see
/// <c>docs/roadmap/plan.md</c>).
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

                services.AddSingleton<IEventBus, EventBus>();

                // Command dispatcher — verb parser + verb → ICommand map. MVP commands
                // register themselves as ICommand in Phase 2 step 8.
                services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            })
            .Build();

        return host.RunAsync();
    }
}
