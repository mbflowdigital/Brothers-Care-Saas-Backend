using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class TenantWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapTenantWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workspace/summary", GetAsync)
            .RequireAuthorization()
            .WithTags("Workspace")
            .WithName("GetTenantWorkspaceSummary")
            .WithSummary("Retorna contagens reais para definir o estado inicial da clínica.")
            .Produces<TenantWorkspaceSummary>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return endpoints;
    }

    private static async Task<Results<Ok<TenantWorkspaceSummary>, ProblemHttpResult>> GetAsync(
        [FromServices] GetTenantWorkspaceSummary query, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await query.ExecuteAsync(cancellationToken);
        return result is not null ? TypedResults.Ok(result) : AuthenticationProblems.Create(context, 403,
            "Selecione uma clínica ativa para continuar.", "active-tenant-required");
    }
}
