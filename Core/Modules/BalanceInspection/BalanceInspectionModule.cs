using Hedron.Core.Commands;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Commands;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>
    /// DI composition entry-point for the balance-inspection surface (prog-3/3b, revised sim-1,
    /// conformance sim-5): the balance-standards store/registry, the core-tier
    /// <see cref="IPowerBudgetSystem"/> oracle (composed from the registry's
    /// <see cref="PowerBudgetTunables"/>), its admin inspector commands, the
    /// <see cref="IBalanceAuditSystem"/> band-drift sweep, and the
    /// <see cref="ITemplateConformanceSystem"/> fitter that corrects what the audit flags.
    /// Call <see cref="AddBalanceInspectionModule"/> from <c>CompositionRoot.Register</c> (NOT
    /// <c>Program.cs</c>) so the <c>Hedron.Web</c> content-authoring host can resolve
    /// <see cref="IPowerBudgetSystem"/>/<see cref="IBalanceAuditSystem"/>/<see cref="IBalanceStandardsRegistry"/>
    /// for the <c>ItemEditor</c>/<c>MobEditor</c> computed-power readout, the <c>Integrity</c> page's
    /// audit report, and the Standards page — a <c>Program.cs</c>-only registration would leave the
    /// web host without them and silently break those surfaces. The inspector commands themselves
    /// are only reachable from the telnet host; registering them here too is inert on the web host.
    /// Mirrors <c>ProgressionModule</c>/<c>AscensionModule</c>.
    /// </summary>
    public static class BalanceInspectionModule
    {
        public static IServiceCollection AddBalanceInspectionModule(this IServiceCollection services)
        {
            services.AddSingleton<IBalanceStandardsStore, BalanceStandardsStore>();

            // Load-once factory: DI resolution of IBalanceStandardsRegistry invokes
            // IBalanceStandardsStore.Load() exactly once (singleton), fails boot fast on
            // structural violations, and logs mirror-drift/ability-kit warnings here — the one
            // place the store's returned warnings become log lines (the store itself never logs).
            services.AddSingleton<IBalanceStandardsRegistry>(sp =>
            {
                var store = sp.GetRequiredService<IBalanceStandardsStore>();
                var logger = sp.GetRequiredService<ILogger<BalanceStandardsRegistry>>();
                var (document, warnings) = store.Load();

                foreach (var warning in warnings)
                    logger.LogWarning("Balance standards: {Warning}", warning);

                logger.LogInformation(
                    "Balance standards loaded: {CellCount} authored cell(s), {WarningCount} drift warning(s).",
                    document.Cells.Count, warnings.Count);

                return new BalanceStandardsRegistry(document);
            });

            // PowerBudgetTunables projected from the registry — Default is no longer directly
            // registered; the registry's compiled-defaults fallback (when no file is present)
            // flows through here instead.
            services.AddSingleton(sp => sp.GetRequiredService<IBalanceStandardsRegistry>().Tunables);

            services.AddSingleton<IPowerBudgetSystem, PowerBudgetSystem>();
            services.AddSingleton<IBalanceAuditSystem>(sp => new BalanceAuditSystem(
                sp.GetRequiredService<ITemplateRegistry>(),
                sp.GetRequiredService<IPowerBudgetSystem>(),
                sp.GetRequiredService<IItemPowerProjectionSystem>(),
                sp.GetRequiredService<IMobPowerProjectionSystem>(),
                sp.GetRequiredService<PowerBudgetTunables>(),
                sp.GetRequiredService<IBalanceStandardsRegistry>().BandDriftTolerance));

            // sim-5: the conformance fitter. Depends on IContentDefinitionCatalog (Authoring
            // module) — both hosts register AuthoringModule alongside this one, so resolution
            // order across AddXModule calls doesn't matter (DI resolves lazily).
            services.AddSingleton<ITemplateConformanceSystem, TemplateConformanceSystem>();

            services.AddSingleton<ICommand, PowerCommand>();
            services.AddSingleton<ICommand, PowerbandCommand>();
            return services;
        }
    }
}
