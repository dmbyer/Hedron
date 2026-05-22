using Hedron.Core.Commands;
using Hedron.Core.Modules.Account.Commands;
using Hedron.Core.Modules.Account.Handlers;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace Hedron.Core.Modules.Account
{
    /// <summary>
    /// DI composition entry point for the Account module.
    /// Call <see cref="AddAccountModule"/> from <c>Server/Program.cs</c>.
    /// </summary>
    public static class AccountModule
    {
        public static IServiceCollection AddAccountModule(this IServiceCollection services)
        {
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddSingleton<IAccountSystem, AccountSystem>();
            services.AddSingleton<CharacterHydrationHandler>();
            services.AddSingleton<ICommand, WhoisCommand>();
            return services;
        }
    }
}
