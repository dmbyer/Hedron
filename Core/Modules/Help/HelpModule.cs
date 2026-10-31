using System;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Help.Commands;
using Hedron.Core.Modules.Help.Systems;
using Hedron.Core.Systems.Definitions;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Help
{
    /// <summary>
    /// DI composition entry point for the Help module: the <c>help</c> and <c>commands</c>
    /// commands, the help lookup system, and the help-topic family (registry, layer reader,
    /// reload participant, authoring). Depends on <c>IEnumerable&lt;ICommand&gt;</c>,
    /// <c>IAuthorizationChecker</c>, <c>IVerbRegistry</c>, and <c>IAbilityRegistry</c> being
    /// registered before this is resolved.
    /// <para>
    /// Both <c>Lazy&lt;IEnumerable&lt;ICommand&gt;&gt;</c> and <c>Lazy&lt;IVerbRegistry&gt;</c>
    /// break the circular dependency that would otherwise exist because <c>help</c> is itself an
    /// <c>ICommand</c> and <c>IVerbRegistry</c> is implemented by <c>CommandDispatcher</c>, which
    /// depends on all <c>ICommand</c> registrations. Every Help consumer of the verb set
    /// (<c>HelpLookupSystem</c>, <c>HelpTopicReloadParticipant</c>, <c>HelpTopicAuthoring</c>) takes
    /// the <c>Lazy&lt;IVerbRegistry&gt;</c> — the participant because <c>ReloadCommand</c> → loader →
    /// participants would otherwise close the cycle too.
    /// </para>
    /// </summary>
    public static class HelpModule
    {
        public static IServiceCollection AddHelpModule(this IServiceCollection services)
        {
            // Lazy breaks the IEnumerable<ICommand> → HelpCommand → IEnumerable<ICommand> cycle.
            services.AddSingleton(sp =>
                new Lazy<IEnumerable<ICommand>>(() => sp.GetServices<ICommand>()));

            // Lazy breaks the HelpCommand → IVerbRegistry → CommandDispatcher → HelpCommand cycle.
            services.AddSingleton(sp =>
                new Lazy<IVerbRegistry>(() => sp.GetRequiredService<IVerbRegistry>()));

            // Help topics: the snapshot registry (concrete for the participant's Publish, interface
            // for readers), the layer reader, and the reload participant the world loader invokes.
            services.AddSingleton<HelpTopicRegistry>();
            services.AddSingleton<IHelpTopicRegistry>(sp => sp.GetRequiredService<HelpTopicRegistry>());
            services.AddSingleton<IHelpTopicLayerReader, HelpTopicLayerReader>();
            services.AddSingleton<IContentReloadParticipant, HelpTopicReloadParticipant>();
            services.AddSingleton<IHelpLookupSystem, HelpLookupSystem>();
            services.AddSingleton<IHelpTopicAuthoring, HelpTopicAuthoring>();

            services.AddSingleton<ICommand, HelpCommand>();
            services.AddSingleton<ICommand, CommandsCommand>();
            return services;
        }
    }
}
