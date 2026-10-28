using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Login;
using BrothersCare.Application.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Identity;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication")
            .WithMetadata(new AuthenticateOptionalSession());
        group.MapCsrfEndpoint();
        group.MapPasswordRecoveryEndpoints();

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("LoginUser")
            .WithSummary("Autentica a pessoa e cria uma sessão por cookie.")
            .Produces<SessionSummary>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapSessionEndpoints();

        group.MapPost("/activation", ActivateAsync)
            .AllowAnonymous()
            .WithName("ActivateAccount")
            .WithSummary("Ativa a conta ou aceita um convite de acesso.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<Results<Ok<SessionSummary>, ProblemHttpResult>> LoginAsync(
        [FromBody] LoginHttpRequest? request,
        HttpContext context,
        [FromServices] LoginUser login,
        [FromServices] SessionCookie cookie,
        [FromServices] LoginRateLimiter rateLimiter,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!IsAllowedOrigin(context, configuration))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Origem da solicitação não permitida.", "forbidden");
        }

        var email = request?.Email;
        var retryAfter = rateLimiter.GetRetryAfterSeconds(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            email is { Length: <= 320 } ? email.Trim().ToUpperInvariant() : "invalid");
        if (retryAfter is { } seconds)
        {
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return AuthenticationProblems.Create(context, StatusCodes.Status429TooManyRequests,
                "Limite de tentativas atingido. Aguarde para tentar novamente.", "rate-limit");
        }

        if (request?.Email is null || request.Password is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe um e-mail e uma senha válidos.", "validation");
        }

        var result = await login.ExecuteAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        if (result.Status == LoginStatus.InvalidInput)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe um e-mail e uma senha válidos.", "validation");
        }

        if (result.Status != LoginStatus.Succeeded || result.Session is null ||
            result.CookieValue is null || result.AbsoluteExpiresAt is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "E-mail ou senha inválidos.", "authentication-required");
        }

        cookie.Write(context, result.CookieValue, result.AbsoluteExpiresAt.Value);
        return TypedResults.Ok(result.Session);
    }

    internal static bool IsAllowedOrigin(HttpContext context, IConfiguration configuration)
    {
        var origins = context.Request.Headers.Origin;
        if (origins.Count == 0)
        {
            return true;
        }

        if (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("https" or "http") || origin.PathAndQuery != "/" ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Fragment))
        {
            return false;
        }

        var authority = origin.GetLeftPart(UriPartial.Authority);
        var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return string.Equals(authority, sameOrigin, StringComparison.OrdinalIgnoreCase) ||
            allowed.Any(value => Uri.TryCreate(value, UriKind.Absolute, out var configured) &&
                string.Equals(configured.GetLeftPart(UriPartial.Authority), authority, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ActivateAsync(
        [FromBody] AccountActivationHttpRequest? request,
        [FromServices] IAccountActivationService activationService,
        [FromServices] AccountActionRateLimiter rateLimiter,
        [FromServices] IAuthenticationAudit audit,
        [FromServices] TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var denied = rateLimiter.Activation(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (denied is { } seconds)
        {
            return AuthenticationProblems.RateLimited(httpContext, seconds);
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrEmpty(request.Password) ||
            string.IsNullOrEmpty(request.PasswordConfirmation))
        {
            return InvalidActivationRequest(httpContext);
        }

        var result = await activationService.ActivateAsync(
            new AccountActivationRequest(request.Token, request.Password, request.PasswordConfirmation),
            cancellationToken);

        if (result == AccountActivationResult.Activated)
        {
            audit.Record(AuthenticationAuditEvent.AccountActivated, timeProvider.GetUtcNow());
        }

        return result is AccountActivationResult.Activated
            ? TypedResults.NoContent()
            : InvalidActivationRequest(httpContext);
    }

    private static ProblemHttpResult InvalidActivationRequest(HttpContext context) => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Não foi possível concluir a ativação ou aceitar o convite.",
        type: "https://api.brotherscare.com/problems/account-activation-invalid",
        extensions: new Dictionary<string, object?>
        {
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountActivationHttpRequest(
    string? Token,
    string? Password,
    string? PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}
