using Hedron.Core.Commands;
using Hedron.Core.Modules.Items.Commands;
using Hedron.Core.Modules.Items.Handlers;
using Hedron.Core.Modules.Items.Resolvers;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Items
{
    /// <summary>
    /// DI composition entry point for the Items module.
    /// Handlers are registered as concrete singletons; subscription happens in Program.cs.
    /// </summary>
    public static class ItemsModule
    {
        public static IServiceCollection AddItemsModule(this IServiceCollection services)
        {
            // Domain systems
            services.AddSingleton<IItemSystem, ItemSystem>();
            services.AddSingleton<IItemBuilderSystem, ItemBuilderSystem>();
            services.AddSingleton<IItemContentWriter, ItemContentWriter>();

            // Resolvers — stateless singletons, injected into commands via constructor
            services.AddSingleton<ItemInRoomResolver>();
            services.AddSingleton<ItemInInventoryResolver>();

            // Admin commands
            services.AddSingleton<ICommand, MkitemCommand>();
            services.AddSingleton<ICommand, SetitemCommand>();

            // Player commands
            services.AddSingleton<ICommand, GetCommand>();
            services.AddSingleton<ICommand, DropCommand>();
            services.AddSingleton<ICommand, InventoryCommand>();

            // Handler (concrete — subscribed in Program.cs)
            services.AddSingleton<ItemInteractionHandler>();

            // YAML deserializer
            services.AddSingleton<ITemplateDeserializer, ItemTemplateDeserializer>();

            return services;
        }
    }
}
