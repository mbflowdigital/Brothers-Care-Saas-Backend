using BrothersCare.Application.Tenancy;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantListStore(BrothersCareDbContext dbContext) : ITenantListStore
{
    public async Task<IReadOnlyList<TenantListItem>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Tenants.AsNoTracking().OrderBy(item => item.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search.Trim()));
        return await query.Select(item => new TenantListItem(
            item.Id,
            item.Name,
            item.Status.ToString(),
            dbContext.TenantMemberships
                .Where(membership => membership.TenantId == item.Id && membership.Role == TenantMembershipRole.ClinicAdministrator)
                .OrderBy(membership => membership.CreatedAt)
                .Select(membership => membership.Status.ToString())
                .FirstOrDefault() ?? "WithoutAdministrator"))
            .ToListAsync(cancellationToken);
    }
}
