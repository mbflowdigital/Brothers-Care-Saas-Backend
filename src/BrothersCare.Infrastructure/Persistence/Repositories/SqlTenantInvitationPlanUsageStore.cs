using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantInvitationPlanUsageStore(BrothersCareDbContext dbContext) : ITenantInvitationPlanUsageStore
{
    public Task<MembershipStatus?> FindMembershipStatusAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.Where(item => item.Id == membershipId && item.TenantId == tenantId)
            .Select(item => (MembershipStatus?)item.Status).SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountMembershipsAsync(Guid tenantId, IReadOnlyList<MembershipStatus> statuses, CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.CountAsync(item => item.TenantId == tenantId && statuses.Contains(item.Status), cancellationToken);
}
