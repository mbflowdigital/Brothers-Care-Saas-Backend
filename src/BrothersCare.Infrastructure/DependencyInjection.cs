using Azure.Identity;
using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Activation;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Invitations;
using BrothersCare.Application.Identity.Login;
using BrothersCare.Application.Identity.PasswordChange;
using BrothersCare.Application.Identity.PasswordRecovery;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Application.Onboarding;
using BrothersCare.Application.Onboarding.Contracts;
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
using Microsoft.Graph;

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
            services.AddScoped<ICommercialOnboardingSource, SqlCommercialOnboardingSource>();
            services.AddScoped<GetOnboardingContext>();
            services.AddScoped<IOnboardingCompletionStore, SqlOnboardingCompletionStore>();
            services.AddScoped<CompleteOnboarding>();
            services.AddScoped<ITenantProvisioningStore, SqlTenantProvisioningStore>();
            services.AddScoped<ICommercialOnboardingStore, SqlCommercialOnboardingStore>();
            services.AddScoped(provider => new CreateCommercialOnboarding(
                provider.GetRequiredService<ICommercialOnboardingStore>(),
                provider.GetRequiredService<ITokenGenerator>(),
                provider.GetService<ICommercialOnboardingSender>(),
                provider.GetRequiredService<TimeProvider>()));
            services.AddScoped(provider => new ResendCommercialOnboardingInvitation(
                provider.GetRequiredService<ICommercialOnboardingStore>(),
                provider.GetRequiredService<ITokenGenerator>(),
                provider.GetService<ICommercialOnboardingSender>(),
                provider.GetRequiredService<TimeProvider>()));
            services.AddScoped<ITenantListStore, SqlTenantListStore>();
            services.AddScoped<ListTenants>();
            services.AddScoped<GetTenant>();
            services.AddScoped<ITenantPreRegistrationStore, SqlTenantPreRegistrationStore>();
            services.AddScoped<ManageTenantPreRegistration>();
            services.AddScoped<ITenantLifecycleStore, SqlTenantLifecycleStore>();
            services.AddScoped<ChangeTenantLifecycle>();
            services.AddScoped<IClinicUnitStore, SqlClinicUnitStore>();
            services.AddScoped<ManageClinicUnits>();
            services.AddScoped<ITenantInvitationStore, SqlTenantInvitationStore>();
            services.AddScoped<ListTenantTeam>();
            services.AddScoped<ManageTenantTeam>();
            services.AddScoped<GetInvitationContext>();
            services.AddScoped<ITenantWorkspaceStore, SqlTenantWorkspaceStore>();
            services.AddScoped<GetTenantWorkspaceSummary>();
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
        var section = configuration.GetSection("Email:MicrosoftGraph");
        if (!section.GetValue("Enabled", false))
        {
            return;
        }

        var tenantId = section["TenantId"];
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];
        var fromAddress = section["FromAddress"];
        var fromName = section["FromName"];
        var activationUrl = configuration["Email:Links:ActivationUrl"];
        var commercialOnboardingUrl = configuration["Email:Links:CommercialOnboardingUrl"];
        var passwordRecoveryUrl = configuration["Email:Links:PasswordRecoveryUrl"];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tenantId)) errors.Add("Email:MicrosoftGraph:TenantId");
        if (string.IsNullOrWhiteSpace(clientId)) errors.Add("Email:MicrosoftGraph:ClientId");
        if (string.IsNullOrWhiteSpace(clientSecret)) errors.Add("Email:MicrosoftGraph:ClientSecret");
        if (string.IsNullOrWhiteSpace(fromAddress)) errors.Add("Email:MicrosoftGraph:FromAddress");
        if (string.IsNullOrWhiteSpace(fromName)) errors.Add("Email:MicrosoftGraph:FromName");
        if (!Uri.TryCreate(activationUrl, UriKind.Absolute, out var activationUri) ||
            activationUri.Scheme is not ("https" or "http")) errors.Add("Email:Links:ActivationUrl");
        if (!Uri.TryCreate(commercialOnboardingUrl, UriKind.Absolute, out var commercialOnboardingUri) ||
            commercialOnboardingUri.Scheme is not ("https" or "http")) errors.Add("Email:Links:CommercialOnboardingUrl");
        if (!Uri.TryCreate(passwordRecoveryUrl, UriKind.Absolute, out var recoveryUri) ||
            recoveryUri.Scheme is not ("https" or "http")) errors.Add("Email:Links:PasswordRecoveryUrl");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Microsoft Graph email is enabled but these settings are invalid or missing: {string.Join(", ", errors)}.");
        }

        var settings = new MicrosoftGraphEmailSettings(
            tenantId!, clientId!, clientSecret!, fromAddress!, fromName!, activationUri!, commercialOnboardingUri!, recoveryUri!);
        var credential = new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret);
        services.AddSingleton(settings);
        services.AddSingleton(new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]));
        services.AddScoped<MicrosoftGraphEmailSender>();
        services.AddScoped<IInvitationSender>(provider => provider.GetRequiredService<MicrosoftGraphEmailSender>());
        services.AddScoped<ICommercialOnboardingSender>(provider => provider.GetRequiredService<MicrosoftGraphEmailSender>());
        services.AddScoped<IPasswordRecoverySender>(provider => provider.GetRequiredService<MicrosoftGraphEmailSender>());
    }
}
