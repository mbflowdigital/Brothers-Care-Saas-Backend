using System.Security.Claims;
using BrothersCare.Application.Identity.PasswordRecovery;
using BrothersCare.Application.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Identity;

public static class PasswordRecoveryEndpoints
{
    public static RouteGroupBuilder MapPasswordRecoveryEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.MapPost("/password-recovery-requests", RequestAsync).AllowAnonymous()
            .WithName("RequestPasswordRecovery")
            .WithSummary("Solicita instruções de recuperação sem revelar a existência da conta.")
            .Produces<PasswordRecoveryRequestResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/password-recovery-confirmations", ConfirmAsync).AllowAnonymous()
            .WithName("ConfirmPasswordRecovery")
            .WithSummary("Redefine a senha por token único e revoga as sessões anteriores.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static async Task<Results<Accepted<PasswordRecoveryRequestResponse>, ProblemHttpResult>> RequestAsync(
        [FromBody] PasswordRecoveryRequestHttpRequest? request,
        HttpContext context,
        [FromServices] AccountActionRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var denied = rateLimiter.RecoveryRequest(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", request?.Email);
        if (denied is { } seconds)
        {
            return AuthenticationProblems.RateLimited(context, seconds);
        }

        var recovery = context.RequestServices.GetService<RequestPasswordRecovery>();
        if (recovery is not null)
        {
            await recovery.ExecuteAsync(request?.Email, cancellationToken);
        }

        return TypedResults.Accepted((string?)null, new PasswordRecoveryRequestResponse(
            "Se a conta estiver apta, você receberá as instruções para redefinir a senha."));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ConfirmAsync(
        [FromBody] PasswordRecoveryConfirmationHttpRequest? request,
        HttpContext context,
        [FromServices] ConfirmPasswordRecovery recovery,
        [FromServices] AccountActionRateLimiter rateLimiter,
        [FromServices] SessionCookie cookie,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var denied = rateLimiter.RecoveryConfirmation(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (denied is { } seconds)
        {
            return AuthenticationProblems.RateLimited(context, seconds);
        }

        if (request?.Token is null || request.Password is null || request.PasswordConfirmation is null ||
            !await recovery.ExecuteAsync(new ConfirmPasswordRecoveryCommand(
                request.Token, request.Password, request.PasswordConfirmation), cancellationToken))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Não foi possível redefinir a senha com os dados informados.", "password-recovery-invalid");
        }

        cookie.Clear(context);
        context.Features.Set<AuthenticatedSession>(null);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        return TypedResults.NoContent();
    }
}
