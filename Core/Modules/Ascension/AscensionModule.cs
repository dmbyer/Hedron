using Hedron.Core.Commands;
using Hedron.Core.Modules.Ascension.Commands;
using Hedron.Core.Modules.Ascension.Handlers;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Ascension
{
    /// <summary>
    /// DI composition entry-point for the Ascension module.
    /// Call <see cref="AddAscensionModule"/> from <c>CompositionRoot.Register</c> (NOT
    /// <c>Program.cs</c>) so that <b>both</b> the gameplay server and the <c>Hedron.Web</c>
    /// content-authoring host compose the ascension types — registering only in
    /// <c>Program.cs</c> would leave the Blazor host's <c>StatSystem</c> silently
    /// under-counting the tier baseline (a latent INV-24 correctness gap), since
    /// <see cref="AscensionEffectContributor"/> is DI-collected into <c>EffectSystem</c> via
    /// <c>IEnumerable&lt;IEffectContributor&gt;</c>. Mirrors <c>ProgressionModule</c>.
    /// </summary>
    public static class AscensionModule
    {
        public static IServiceCollection AddAscensionModule(this IServiceCollection services)
        {
            services.AddSingleton<IAscensionSystem, AscensionSystem>();
            services.AddSingleton<IEffectContributor, AscensionEffectContributor>();
            services.AddSingleton<AscensionNarrationHandler>();
            services.AddSingleton<ICommand, AscendCommand>();
            return services;
        }
    }
}
