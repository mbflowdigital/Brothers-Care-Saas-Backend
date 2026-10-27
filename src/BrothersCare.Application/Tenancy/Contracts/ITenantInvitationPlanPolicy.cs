using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ITenantInvitationPlanPolicy
{
    Task<InvitationPlanDecision> EvaluateAsync(
        Guid tenantId, Guid? existingMembershipId, TenantMembershipRole role,
        IReadOnlyList<Guid> clinicUnitIds, CancellationToken cancellationToken);
}

public enum InvitationPlanDecision
{
    Allowed = 1,
    LimitExceeded = 2,
    Unavailable = 3,
}
