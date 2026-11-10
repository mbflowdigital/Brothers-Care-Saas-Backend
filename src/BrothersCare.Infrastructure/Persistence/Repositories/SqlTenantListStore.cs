using BrothersCare.Application.Tenancy;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantListStore(BrothersCareDbContext dbContext) : ITenantListStore
{
    public async Task<TenantPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var allTenants = dbContext.Tenants.AsNoTracking();
        var query = allTenants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search));

        var administrators =
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.Role == TenantMembershipRole.ClinicAdministrator
            select new { membership.TenantId, membership.CreatedAt, membership.Status, user.DisplayName, user.Email };

        var rows = await query.OrderBy(item => item.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new TenantListRow(
                item.Id, item.Name, item.Cnpj, item.Status.ToString(),
                administrators.Where(admin => admin.TenantId == item.Id).OrderBy(admin => admin.CreatedAt)
                    .Select(admin => admin.Status.ToString()).FirstOrDefault() ?? "WithoutAdministrator",
                administrators.Where(admin => admin.TenantId == item.Id).OrderBy(admin => admin.CreatedAt)
                    .Select(admin => admin.DisplayName).FirstOrDefault(),
                administrators.Where(admin => admin.TenantId == item.Id).OrderBy(admin => admin.CreatedAt)
                    .Select(admin => admin.Email).FirstOrDefault(),
                query.Count(),
                allTenants.Count(tenant => tenant.Status == TenantStatus.PendingOnboarding),
                allTenants.Count(tenant => tenant.Status == TenantStatus.Active)))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            if (page > 1) return await ListAsync(search, page - 1, pageSize, cancellationToken);
            var counts = await allTenants.GroupBy(_ => 1).Select(group => new
            {
                Pending = group.Count(item => item.Status == TenantStatus.PendingOnboarding),
                Active = group.Count(item => item.Status == TenantStatus.Active),
            }).SingleOrDefaultAsync(cancellationToken);
            return new TenantPage([], 1, pageSize, 0, 1, counts?.Pending ?? 0, counts?.Active ?? 0);
        }

        var first = rows[0];
        var totalPages = Math.Max(1, (int)Math.Ceiling(first.TotalItems / (double)pageSize));
        var items = rows.Select(item => new TenantListItem(item.Id, item.Name, item.Cnpj, item.Status,
            item.FirstAdministratorStatus, item.ResponsibleName, item.ResponsibleEmail)).ToArray();
        return new TenantPage(items, page, pageSize, first.TotalItems, totalPages, first.PendingCount, first.ActiveCount);
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

    private sealed record TenantListRow(
        Guid Id, string Name, string? Cnpj, string Status, string FirstAdministratorStatus,
        string? ResponsibleName, string? ResponsibleEmail, int TotalItems, int PendingCount, int ActiveCount);
}
