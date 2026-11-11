using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class TenantListEndpoints
{
    public static IEndpointRouteBuilder MapTenantListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenants", async (
            [FromQuery] string? search,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] ListTenants listTenants,
            CancellationToken ct) =>
        {
            return Results.Ok(await listTenants.ExecuteAsync(search, page == 0 ? 1 : page, pageSize == 0 ? 10 : pageSize, ct));
        }).RequireAuthorization(IdentityPolicies.SuperAdministrator)
            .WithTags("Empresas")
            .WithName("ListTenants")
            .WithSummary("Lista as empresas cadastradas na plataforma.");

        endpoints.MapGet("/api/v1/tenants/{tenantId:guid}", GetAsync)
            .RequireAuthorization(IdentityPolicies.SuperAdministrator)
            .WithTags("Empresas")
            .WithName("GetTenant")
            .WithSummary("Consulta os dados administrativos de uma empresa.")
            .WithDescription("Não retorna pacientes, prontuários, evoluções ou outros dados clínicos.")
            .Produces<TenantDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Results<Ok<TenantDetails>, ProblemHttpResult>> GetAsync(
        Guid tenantId,
        [FromServices] GetTenant getTenant,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var tenant = await getTenant.ExecuteAsync(tenantId, cancellationToken);
        return tenant is null
            ? AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Empresa não encontrada.", "resource-not-found")
            : TypedResults.Ok(tenant);
    }
}

