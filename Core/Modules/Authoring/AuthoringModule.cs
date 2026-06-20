using Hedron.Core.Modules.Authoring.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// DI composition entry point for the Authoring module: the cross-kind content-definition
    /// catalog that the offline editor and the bulk generator both call. The content validator
    /// it depends on is registered by the World module (its home).
    /// </summary>
    public static class AuthoringModule
    {
        public static IServiceCollection AddAuthoringModule(this IServiceCollection services)
        {
            services.AddSingleton<IContentReferenceIndex, ContentReferenceIndex>();
            services.AddSingleton<IContentDefinitionCatalog, ContentDefinitionCatalog>();
            services.AddSingleton<IContentGenerationSystem, ContentGenerationSystem>();
            return services;
        }
    }
}
