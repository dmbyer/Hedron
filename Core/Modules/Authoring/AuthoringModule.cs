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
            // Infrastructure port, not a domain system (no reference/systems.md row) — registered
            // so the composition-root smoke guard covers the catalog's read seam.
            services.AddSingleton<IContentFileReader, ContentFileReader>();
            services.AddSingleton<IContentReferenceIndex, ContentReferenceIndex>();
            services.AddSingleton<IContentDefinitionCatalog, ContentDefinitionCatalog>();
            services.AddSingleton<IContentGenerationSystem, ContentGenerationSystem>();
            services.AddSingleton<IAreaLayoutSystem, AreaLayoutSystem>();
            return services;
        }
    }
}
