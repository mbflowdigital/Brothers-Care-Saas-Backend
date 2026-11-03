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

    public async Task<TenantDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => new { item.Id, item.Name, Status = item.Status.ToString() })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var administrator = await (
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.TenantId == tenantId && membership.Role == TenantMembershipRole.ClinicAdministrator
            orderby membership.CreatedAt
            select new TenantAdministratorDetails(
                membership.Id,
                user.DisplayName,
                user.Email,
                membership.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

        return new TenantDetails(tenant.Id, tenant.Name, tenant.Status, administrator);
    }
}
