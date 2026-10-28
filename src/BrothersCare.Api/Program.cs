using BrothersCare.Application;
using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Api.Identity;
using BrothersCare.Api.Middleware;
using BrothersCare.Api.Tenancy;
using BrothersCare.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var isAdministrativeCommand = args.Contains("--bootstrap-super-administrator", StringComparer.Ordinal) ||
    args.Contains("--issue-super-administrator-activation-token", StringComparer.Ordinal);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, configureEmail: !isAdministrativeCommand);
builder.Services.AddSingleton<SessionCookie>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddSingleton<AccountActionRateLimiter>();
builder.Services.AddSessionAntiforgery(builder.Configuration, builder.Environment);
builder.Services.AddHsts(_ => { });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(IdentityPolicies.SuperAdministrator, policy =>
        policy.RequireAuthenticatedUser().RequireClaim("platform_role", "SuperAdministrator"));
    options.AddPolicy(IdentityPolicies.ClinicAdministrator, policy =>
        policy.RequireAuthenticatedUser().RequireClaim("tenant_role", "ClinicAdministrator").RequireClaim("active_tenant_id"));
});

var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Select(value =>
    {
        if (value.Contains('*') || !Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("http" or "https") || origin.PathAndQuery != "/" ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException("CORS requires explicit HTTP or HTTPS origins without wildcards or paths.");
        }

        return origin.GetLeftPart(UriPartial.Authority);
    }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .WithHeaders("Content-Type", "X-CSRF-Token")
                .WithMethods("GET", "POST", "PUT")
                .WithExposedHeaders("Retry-After")
                .AllowCredentials()));
}

var app = builder.Build();

if (args.Contains("--bootstrap-super-administrator", StringComparer.Ordinal))
{
    const string expectedConfirmation = "create-super-administrator";
    var bootstrapConfiguration = app.Configuration.GetSection("Bootstrap:SuperAdministrator");
    var confirmation = bootstrapConfiguration["Confirmation"];
    var email = bootstrapConfiguration["Email"];
    var displayName = bootstrapConfiguration["DisplayName"];

    if (!string.Equals(confirmation, expectedConfirmation, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Set Bootstrap__SuperAdministrator__Confirmation before bootstrapping a platform administrator.");
    }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName))
    {
        throw new InvalidOperationException("Set the bootstrap email and display name before bootstrapping a platform administrator.");
    }

    using var scope = app.Services.CreateScope();
    var bootstrapper = scope.ServiceProvider.GetService<IPlatformAdministratorBootstrapper>()
        ?? throw new InvalidOperationException("Configure ConnectionStrings__BrothersCare before bootstrapping a platform administrator.");
    var result = await bootstrapper.EnsureSuperAdministratorAsync(
        new PlatformAdministratorBootstrapRequest(email, displayName),
        CancellationToken.None);

    Console.WriteLine(result.Created
        ? "Platform administrator bootstrap completed."
        : "Platform administrator already exists.");
    return;
}

if (args.Contains("--issue-super-administrator-activation-token", StringComparer.Ordinal))
{
    const string expectedConfirmation = "issue-account-activation-token";
    var activationConfiguration = app.Configuration.GetSection("Bootstrap:AccountActivation");
    var confirmation = activationConfiguration["Confirmation"];
    var email = activationConfiguration["Email"];
    var lifetimeMinutes = activationConfiguration.GetValue<int?>("LifetimeMinutes") ?? 30;

    if (!string.Equals(confirmation, expectedConfirmation, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Set Bootstrap__AccountActivation__Confirmation before issuing an activation token.");
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        throw new InvalidOperationException("Set the platform administrator email before issuing an activation token.");
    }

    using var scope = app.Services.CreateScope();
    var issuer = scope.ServiceProvider.GetService<IPlatformAdministratorActivationTokenIssuer>()
        ?? throw new InvalidOperationException("Configure ConnectionStrings__BrothersCare before issuing an activation token.");
    var result = await issuer.IssueAsync(
        new PlatformAdministratorActivationTokenIssueRequest(email, TimeSpan.FromMinutes(lifetimeMinutes)),
        CancellationToken.None);

    Console.WriteLine("Activation token (copy now; it will not be shown again):");
    Console.WriteLine(result.Token);
    Console.WriteLine($"Expires at: {result.ExpiresAt:O}");
    return;
}

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CsrfProtectionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
}).AllowAnonymous();
app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapTenantListEndpoints();
app.MapInvitationEndpoints();

app.Run();

public partial class Program;
