using Hedron.Core.Commands;
using Hedron.Core.Modules.World.Commands;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.World
{
    /// <summary>
    /// DI composition entry point for the World module: cross-cutting template registry,
    /// YAML content serializer, content loader, and the existing <c>look</c> command.
    /// </summary>
    /// <remarks>
    /// <see cref="ITemplateRegistry"/> lives at <c>Core/Systems/</c> because every future
    /// content-bearing module (mobs, items, shops) registers into the same registry; the
    /// World module owns the registration here as the first consumer.
    /// </remarks>
    public static class WorldModule
    {
        public static IServiceCollection AddWorldModule(this IServiceCollection services)
        {
            services.AddSingleton<ITemplateRegistry, TemplateRegistry>();
            services.AddSingleton<IContentSerializer, YamlContentSerializer>();
            services.AddSingleton<IWorldContentLoader, WorldContentLoader>();
            services.AddSingleton<IRoomContentWriter, RoomContentWriter>();

            // Per-kind deserializers — the World module owns the room/area kinds. Future
            // modules (mobs, items) register their own kinds the same way without touching
            // the cross-cutting YamlContentSerializer.
            services.AddSingleton<ITemplateDeserializer, RoomTemplateDeserializer>();
            services.AddSingleton<ITemplateDeserializer, AreaTemplateDeserializer>();

            services.AddSingleton<ICommand, LookCommand>();
            return services;
        }
    }
}
