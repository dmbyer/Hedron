using Hedron.Core.Modules.Simulation.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Simulation
{
    /// <summary>
    /// DI composition entry-point for the simulation engine (sim-2). Call
    /// <see cref="AddSimulationModule"/> from <c>CompositionRoot.Register</c> (NOT <c>Program.cs</c>)
    /// so <c>Hedron.Web</c> can resolve <see cref="ISimulationRunner"/> directly at sim-3 — exactly
    /// how <c>BalanceInspectionModule</c>/<c>ProgressionModule</c> serve both hosts. No hosted
    /// service, no command, no handler: the engine is a pure callee (INV-5/INV-10), reached today
    /// only by the <c>simulate</c> run-mode.
    /// </summary>
    public static class SimulationModule
    {
        public static IServiceCollection AddSimulationModule(this IServiceCollection services)
        {
            services.AddSingleton<ISimCombatantPolicy, MeleeOnlyPolicy>();
            services.AddSingleton<ISimCombatantPolicy, RoundRobinPolicy>();
            services.AddSingleton<ISimCombatantPolicy, CooldownFirstPolicy>();

            services.AddSingleton<ISimScenarioStore, SimScenarioStore>();
            services.AddSingleton<ISandboxWorldFactory, SandboxWorldFactory>();
            services.AddSingleton<ISimCombatantFactory, SimCombatantFactory>();
            services.AddSingleton<ISimOutcomeEvaluator, SimOutcomeEvaluator>();
            services.AddSingleton<ISimulationRunner, SimulationRunner>();
            services.AddSingleton<ISimReportWriter, SimReportWriter>();

            return services;
        }
    }
}
