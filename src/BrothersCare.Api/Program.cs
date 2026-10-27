using BrothersCare.Application;
using BrothersCare.Application.Identity;
using BrothersCare.Api.Identity;
using BrothersCare.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()));
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});
app.MapAuthEndpoints();

app.Run();

public partial class Program;
