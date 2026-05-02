using Hedron.Core.Handlers;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Persistence
{
    /// <summary>
    /// DI composition entry point for the persistence substrate.
    /// Call <see cref="AddPersistenceModule"/> from <c>Server/Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// The two hosted services (<c>PersistenceBootstrap</c> and <c>PersistenceFlushTimer</c>)
    /// are registered as hosted services in <c>Server/Program.cs</c> rather than here because
    /// they live in the <c>Server</c> project and load ordering relative to <c>TelnetServer</c>
    /// must be controlled at the composition root.
    /// </remarks>
    public static class PersistenceModule
    {
        public static IServiceCollection AddPersistenceModule(this IServiceCollection services)
        {
            services.AddSingleton<IComponentTypeRegistry, ComponentTypeRegistry>();
            services.AddSingleton<IComponentSerializer, ComponentSerializer>();
            services.AddSingleton<IPersistenceSystem, PersistenceSystem>();
            services.AddSingleton<PersistenceHandler>();
            return services;
        }
    }
}
