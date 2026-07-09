using Hedron.Core.Commands;
using Hedron.Core.Modules.BalanceInspection.Commands;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>
    /// DI composition entry-point for the balance-inspection surface (prog-3/3b): the core-tier
    /// <see cref="IPowerBudgetSystem"/> oracle, its admin inspector commands, and the
    /// <see cref="IBalanceAuditSystem"/> band-drift sweep.
    /// Call <see cref="AddBalanceInspectionModule"/> from <c>CompositionRoot.Register</c> (NOT
    /// <c>Program.cs</c>) so the <c>Hedron.Web</c> content-authoring host can resolve
    /// <see cref="IPowerBudgetSystem"/>/<see cref="IBalanceAuditSystem"/> for the
    /// <c>ItemEditor</c>/<c>MobEditor</c> computed-power readout and the <c>Integrity</c> page's
    /// audit report — a <c>Program.cs</c>-only registration would leave the web host without them
    /// and silently break those surfaces. The inspector commands themselves are only reachable from
    /// the telnet host; registering them here too is inert on the web host. Mirrors
    /// <c>ProgressionModule</c>/<c>AscensionModule</c>.
    /// </summary>
    public static class BalanceInspectionModule
    {
        public static IServiceCollection AddBalanceInspectionModule(this IServiceCollection services)
        {
            services.AddSingleton<IPowerBudgetSystem, PowerBudgetSystem>();
            services.AddSingleton<IBalanceAuditSystem, BalanceAuditSystem>();
            services.AddSingleton<ICommand, PowerCommand>();
            services.AddSingleton<ICommand, PowerbandCommand>();
            return services;
        }
    }
}
