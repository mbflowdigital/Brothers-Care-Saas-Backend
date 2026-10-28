using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ITenantInvitationPlanUsageStore
{
    Task<MembershipStatus?> FindMembershipStatusAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task<int> CountMembershipsAsync(Guid tenantId, IReadOnlyList<MembershipStatus> statuses, CancellationToken cancellationToken);
}
