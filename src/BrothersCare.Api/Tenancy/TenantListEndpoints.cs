using BrothersCare.Infrastructure.Persistence;
using BrothersCare.Api.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Api.Tenancy;

public static class TenantListEndpoints
{
    public static IEndpointRouteBuilder MapTenantListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenants", async ([FromQuery] string? search, [FromServices] BrothersCareDbContext db, CancellationToken ct) =>
        {
            var query = db.Tenants.AsNoTracking().OrderBy(item => item.Name).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search.Trim()));
            var items = await query.Select(item => new TenantListItem(item.Id, item.Name, item.Status.ToString())).ToListAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization(IdentityPolicies.SuperAdministrator);
        return endpoints;
    }
}

public sealed record TenantListItem(Guid Id, string Name, string Status);
