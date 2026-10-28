using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class TenantListEndpoints
{
    public static IEndpointRouteBuilder MapTenantListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenants", async ([FromQuery] string? search, [FromServices] ListTenants listTenants, CancellationToken ct) =>
        {
            return Results.Ok(await listTenants.ExecuteAsync(search, ct));
        }).RequireAuthorization(IdentityPolicies.SuperAdministrator);
        return endpoints;
    }
}

