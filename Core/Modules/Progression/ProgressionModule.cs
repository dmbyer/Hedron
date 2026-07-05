using Hedron.Core.Commands;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Progression.Commands;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// DI composition entry-point for the Progression module.
    /// Call <see cref="AddProgressionModule"/> from <c>CompositionRoot.Register</c> so that
    /// <b>both</b> the gameplay server and the <c>Hedron.Web</c> content-authoring host compose
    /// the progression types — registering only in <c>Program.cs</c> would leave the Blazor host's
    /// <c>StatSystem</c> silently under-counting progression (a latent INV-24 correctness gap),
    /// since <see cref="ProgressionEffectContributor"/> is DI-collected into <c>EffectSystem</c>
    /// via <c>IEnumerable&lt;IEffectContributor&gt;</c>. Mirrors <c>EconomyModule</c>.
    /// </summary>
    public static class ProgressionModule
    {
        public static IServiceCollection AddProgressionModule(this IServiceCollection services)
        {
            services.AddSingleton<IProgressionSystem, ProgressionSystem>();
            services.AddSingleton<IEffectContributor, ProgressionEffectContributor>();
            services.AddSingleton<ExperienceAwardHandler>();
            services.AddSingleton<ICommand, ProgressCommand>();
            return services;
        }
    }
}
