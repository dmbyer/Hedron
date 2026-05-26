using Hedron.Core.Commands;
using Hedron.Core.Modules.Attributes.Commands;
using Hedron.Core.Modules.Attributes.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Attributes
{
    public static class AttributesModule
    {
        public static IServiceCollection AddAttributesModule(this IServiceCollection services)
        {
            services.AddSingleton<IAttributeSystem, AttributeSystem>();
            services.AddSingleton<ICommand, ScoreCommand>();
            services.AddSingleton<ICommand, SetPlayerCommand>();
            return services;
        }
    }
}
