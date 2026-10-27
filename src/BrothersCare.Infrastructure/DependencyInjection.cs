using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Activation;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Invitations;
using BrothersCare.Application.Identity.Login;
using BrothersCare.Application.Identity.PasswordChange;
using BrothersCare.Application.Identity.PasswordRecovery;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Application.Tenancy;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Infrastructure.Identity;
using BrothersCare.Infrastructure.Persistence;
using BrothersCare.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrothersCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton<IAuthenticationAudit, AuthenticationAudit>();

        var connectionString = configuration.GetConnectionString("BrothersCare");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.TryAddSingleton(new AuthenticationSettings(
                configuration.GetValue("Authentication:MaximumFailedAttempts", 5),
                configuration.GetValue("Authentication:LockoutMinutes", 15),
                configuration.GetValue("Authentication:IdleTimeoutMinutes", 30),
                configuration.GetValue("Authentication:AbsoluteLifetimeHours", 12),
                configuration.GetValue("Authentication:PasswordRecoveryTokenMinutes", 30)));
            services.AddScoped<IAuthenticationStore, SqlAuthenticationStore>();
            services.AddScoped<LoginUser>();
            services.AddScoped<GetCurrentSession>();
            services.AddScoped<SelectActiveTenant>();
            services.AddScoped<LogoutUser>();
            services.AddScoped<ChangePassword>();
            services.AddScoped<IPasswordRecoveryStore, SqlPasswordRecoveryStore>();
            services.AddScoped<ConfirmPasswordRecovery>();
            services.AddScoped(provider => new RequestPasswordRecovery(
                provider.GetRequiredService<IPasswordRecoveryStore>(),
                provider.GetRequiredService<ITokenGenerator>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<AuthenticationSettings>(),
                provider.GetService<IPasswordRecoverySender>()));
            services.TryAddSingleton<ITokenGenerator, InvitationTokenService>();
            services.AddScoped<ITenantProvisioningStore, SqlTenantProvisioningStore>();
            services.AddScoped<ITenantInvitationStore, SqlTenantInvitationStore>();
            services.AddScoped<ITenantInvitationPlanUsageStore, SqlTenantInvitationPlanUsageStore>();
            services.TryAddSingleton(new ProvisionalInvitationPlanSettings(
                // Valor provisório por empresa; substituir quando os limites dos planos forem definidos.
                configuration.GetValue("Tenancy:ProvisionalPlan:MaximumMemberships", 25)));
            services.TryAddScoped<ITenantInvitationPlanPolicy, ProvisionalTenantInvitationPlanPolicy>();
            services.AddScoped(provider => new InviteTenantUser(
                provider.GetRequiredService<ICurrentPrincipalAccessor>(),
                provider.GetRequiredService<ITenantInvitationStore>(),
                provider.GetRequiredService<ITenantInvitationPlanPolicy>(),
                provider.GetRequiredService<ITokenGenerator>(),
                provider.GetService<IInvitationSender>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IAuthenticationAudit>()));
            services.AddScoped(provider => new CreateTenantWithAdministrator(
                provider.GetRequiredService<ITenantProvisioningStore>(),
                provider.GetRequiredService<ITokenGenerator>(),
                provider.GetService<IInvitationSender>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ICurrentPrincipalAccessor>(),
                provider.GetRequiredService<IAuthenticationAudit>()));
            services.AddDbContext<BrothersCareDbContext>(options => options.UseSqlServer(connectionString));
            services.AddScoped<IPlatformAdministratorStore, SqlPlatformAdministratorStore>();
            services.AddScoped<IPlatformAdministratorBootstrapper, PlatformAdministratorBootstrapper>();
            services.TryAddScoped<IPasswordHasher, PasswordHasher>();
            services.AddScoped<IAccountActivationStore, SqlAccountActivationStore>();
            services.AddScoped<IAccountActivationService, ActivateAccount>();
            services.AddScoped<IPlatformAdministratorActivationTokenIssuer, PlatformAdministratorActivationTokenIssuer>();
        }

        return services;
    }
}
