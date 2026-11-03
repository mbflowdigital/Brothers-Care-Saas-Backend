using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class TenantLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapTenantLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/tenants/{tenantId:guid}/lifecycle", ChangeAsync)
            .RequireAuthorization(IdentityPolicies.SuperAdministrator)
            .WithTags("Empresas")
            .WithName("ChangeTenantLifecycle")
            .WithSummary("Suspende, reativa ou desativa uma empresa.")
            .WithDescription("Suspender ou desativar revoga sessões cujo tenant ativo é a empresa afetada. Nenhum dado clínico é retornado.")
            .Produces<TenantLifecycleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<Results<Ok<TenantLifecycleResponse>, ProblemHttpResult>> ChangeAsync(
        Guid tenantId,
        [FromBody] TenantLifecycleRequest? request,
        [FromServices] ChangeTenantLifecycle changeTenantLifecycle,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request is null || !Enum.TryParse<TenantLifecycleAction>(request.Action, true, out var action))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe uma ação válida para a empresa.", "validation");
        }

        var result = await changeTenantLifecycle.ExecuteAsync(tenantId, action, cancellationToken);
        return result.Status switch
        {
            TenantLifecycleStatus.Changed when result.TenantStatus is { } status =>
                TypedResults.Ok(new TenantLifecycleResponse(tenantId, status.ToString())),
            TenantLifecycleStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required"),
            TenantLifecycleStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Você não tem permissão para alterar o status desta empresa.", "forbidden"),
            TenantLifecycleStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Empresa não encontrada.", "resource-not-found"),
            TenantLifecycleStatus.InvalidState => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "A transição de status solicitada não é permitida.", "invalid-state-transition"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe uma ação válida para a empresa.", "validation"),
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantLifecycleRequest(string? Action);

public sealed record TenantLifecycleResponse(Guid TenantId, string Status);
