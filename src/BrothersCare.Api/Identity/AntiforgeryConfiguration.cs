using Microsoft.AspNetCore.Antiforgery;

namespace BrothersCare.Api.Identity;

public static class AntiforgeryConfiguration
{
    public static IServiceCollection AddSessionAntiforgery(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var secure = configuration.GetValue("Authentication:CookieSecure", true);
        if (!secure && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Antiforgery cookies must be secure outside Development.");
        }

        if (string.Equals(configuration["Authentication:CookieName"], "bc_csrf", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Session and antiforgery cookies must have different names.");
        }

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "bc_csrf";
            options.Cookie.Path = "/api";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
        });
        return services;
    }

    public static RouteGroupBuilder MapCsrfEndpoint(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.MapGet("/csrf-token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!, "X-CSRF-Token"));
        }).AllowAnonymous().WithName("GetCsrfToken")
            .WithSummary("Obtém o token de proteção das requisições de escrita.")
            .Produces<CsrfTokenResponse>();
        return group;
    }
}

internal sealed class AuthenticateOptionalSession { }

public sealed record CsrfTokenResponse(string Token, string HeaderName)
{
    public override string ToString() => "[REDACTED]";
}
