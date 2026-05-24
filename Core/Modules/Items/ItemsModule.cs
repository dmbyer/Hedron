using Hedron.Core.Commands;
using Hedron.Core.Modules.Items.Commands;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Items
{
    /// <summary>
    /// DI composition entry point for the Items module: builder system, query system,
    /// admin commands, and the YAML template deserializer.
    /// </summary>
    public static class ItemsModule
    {
        public static IServiceCollection AddItemsModule(this IServiceCollection services)
        {
            services.AddSingleton<IItemSystem, ItemSystem>();
            services.AddSingleton<IItemBuilderSystem, ItemBuilderSystem>();

            services.AddSingleton<ICommand, MkitemCommand>();
            services.AddSingleton<ICommand, SetitemCommand>();

            services.AddSingleton<ITemplateDeserializer, ItemTemplateDeserializer>();

            return services;
        }
    }
}
