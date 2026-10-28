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
using BrothersCare.Infrastructure.Email;
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
        IConfiguration configuration,
        bool configureEmail = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton<IAuthenticationAudit, AuthenticationAudit>();
        if (configureEmail)
        {
            ConfigureEmail(services, configuration);
        }

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
            services.AddScoped<ITenantListStore, SqlTenantListStore>();
            services.AddScoped<ListTenants>();
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

    private static void ConfigureEmail(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Email:Smtp");
        if (!section.GetValue("Enabled", false))
        {
            return;
        }

        var host = section["Host"];
        var userName = section["UserName"];
        var password = section["Password"];
        var fromAddress = section["FromAddress"];
        var fromName = section["FromName"];
        var port = section.GetValue<int?>("Port") ?? 587;
        var activationUrl = configuration["Email:Links:ActivationUrl"];
        var passwordRecoveryUrl = configuration["Email:Links:PasswordRecoveryUrl"];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(host)) errors.Add("Email:Smtp:Host");
        if (string.IsNullOrWhiteSpace(userName)) errors.Add("Email:Smtp:UserName");
        if (string.IsNullOrWhiteSpace(password)) errors.Add("Email:Smtp:Password");
        if (string.IsNullOrWhiteSpace(fromAddress)) errors.Add("Email:Smtp:FromAddress");
        if (string.IsNullOrWhiteSpace(fromName)) errors.Add("Email:Smtp:FromName");
        if (port is < 1 or > 65535) errors.Add("Email:Smtp:Port");
        if (!Uri.TryCreate(activationUrl, UriKind.Absolute, out var activationUri) ||
            activationUri.Scheme is not ("https" or "http")) errors.Add("Email:Links:ActivationUrl");
        if (!Uri.TryCreate(passwordRecoveryUrl, UriKind.Absolute, out var recoveryUri) ||
            recoveryUri.Scheme is not ("https" or "http")) errors.Add("Email:Links:PasswordRecoveryUrl");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Email SMTP is enabled but these settings are invalid or missing: {string.Join(", ", errors)}.");
        }

        services.AddSingleton(new SmtpEmailSettings(
            host!, port, userName!, password!, fromAddress!, fromName!, activationUri!, recoveryUri!));
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IInvitationSender>(provider => provider.GetRequiredService<SmtpEmailSender>());
        services.AddScoped<IPasswordRecoverySender>(provider => provider.GetRequiredService<SmtpEmailSender>());
    }
}
