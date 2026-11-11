using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantWorkspaceStore(BrothersCareDbContext dbContext) : ITenantWorkspaceStore
{
    public async Task<TenantWorkspaceCounts> CountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var team = await dbContext.TenantMemberships.AsNoTracking().CountAsync(item => item.TenantId == tenantId && item.Status == MembershipStatus.Active, cancellationToken);
        var patients = await dbContext.Patients.AsNoTracking().CountAsync(item => item.TenantId == tenantId, cancellationToken);
        var appointments = await dbContext.Appointments.AsNoTracking().CountAsync(item => item.TenantId == tenantId, cancellationToken);
        return new(team, patients, appointments);
    }
}
