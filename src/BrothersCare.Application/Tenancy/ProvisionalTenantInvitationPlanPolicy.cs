using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class ProvisionalTenantInvitationPlanPolicy(
    ITenantInvitationPlanUsageStore store,
    ProvisionalInvitationPlanSettings settings) : ITenantInvitationPlanPolicy
{
    private static readonly MembershipStatus[] ReservedStates =
        [MembershipStatus.Invited, MembershipStatus.Active, MembershipStatus.Suspended];

    public async Task<InvitationPlanDecision> EvaluateAsync(
        Guid tenantId, Guid? existingMembershipId, TenantMembershipRole role,
        IReadOnlyList<Guid> clinicUnitIds, CancellationToken cancellationToken)
    {
        var existingStatus = existingMembershipId is { } membershipId
            ? await store.FindMembershipStatusAsync(tenantId, membershipId, cancellationToken)
            : null;
        if (existingMembershipId is not null && existingStatus is null)
        {
            return InvitationPlanDecision.Unavailable;
        }

        var occupied = await store.CountMembershipsAsync(tenantId, ReservedStates, cancellationToken);
        var additionalSeat = existingStatus is { } status && ReservedStates.Contains(status) ? 0 : 1;
        return (long)occupied + additionalSeat <= settings.MaximumMemberships
            ? InvitationPlanDecision.Allowed : InvitationPlanDecision.LimitExceeded;
    }
}

public sealed class ProvisionalInvitationPlanSettings
{
    // Limite provisório de 25 vínculos por empresa, até a definição dos planos e da regra final de contagem.
    public ProvisionalInvitationPlanSettings(int maximumMemberships = 25)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMemberships);
        MaximumMemberships = maximumMemberships;
    }

    public int MaximumMemberships { get; }
}
