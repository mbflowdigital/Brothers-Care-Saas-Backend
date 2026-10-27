using System.Security.Claims;
using BrothersCare.Application.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Identity;

public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.MapGet("/session", GetAsync)
            .RequireAuthorization()
            .WithName("GetCurrentSession")
            .WithSummary("Consulta a identidade e os vínculos ativos da sessão.")
            .Produces<SessionSummary>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPut("/active-tenant", SelectTenantAsync)
            .RequireAuthorization()
            .WithName("SelectActiveTenant")
            .WithSummary("Seleciona uma empresa com vínculo ativo da pessoa autenticada.")
            .Produces<SessionSummary>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("LogoutUser")
            .WithSummary("Revoga a sessão atual e remove o cookie de autenticação.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return group;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LogoutAsync(
        HttpContext context,
        [FromServices] SessionCookie cookie,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!AuthEndpoints.IsAllowedOrigin(context, configuration))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Origem da solicitação não permitida.", "forbidden");
        }

        var value = cookie.Read(context);
        if (SessionToken.TryParse(value, out _))
        {
            // Resolve persistence only when the cookie could identify a server-side session.
            var logout = context.RequestServices.GetService<LogoutUser>();
            if (logout is null)
            {
                return AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                    "Não foi possível encerrar a sessão. Tente novamente.", "service-unavailable");
            }

            await logout.ExecuteAsync(value, cancellationToken);
        }

        cookie.Clear(context);
        context.Features.Set<AuthenticatedSession>(null);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        return TypedResults.NoContent();
    }

    private static Results<Ok<SessionSummary>, ProblemHttpResult> GetAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var summary = context.Features.Get<AuthenticatedSession>()?.Summary;
        if (summary is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required");
        }

        return TypedResults.Ok(summary);
    }

    private static async Task<Results<Ok<SessionSummary>, ProblemHttpResult>> SelectTenantAsync(
        [FromBody] SelectActiveTenantHttpRequest? request,
        [FromServices] SelectActiveTenant selectActiveTenant,
        [FromServices] SessionCookie cookie,
        [FromServices] IConfiguration configuration,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!AuthEndpoints.IsAllowedOrigin(context, configuration))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Origem da solicitação não permitida.", "forbidden");
        }

        if (request is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe uma empresa válida.", "validation");
        }

        var result = await selectActiveTenant.ExecuteAsync(request.TenantId, cancellationToken);
        if (result.Status == TenantSelectionStatus.Selected && result.Session is not null)
        {
            return TypedResults.Ok(result.Session);
        }

        if (result.Status == TenantSelectionStatus.Unauthenticated)
        {
            cookie.Clear(context);
            return AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required");
        }

        return result.Status == TenantSelectionStatus.InvalidInput
            ? AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe uma empresa válida.", "validation")
            : AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Empresa não encontrada ou indisponível para seleção.", "not-found");
    }
}
