using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Invitations;

public sealed class ManageTenantTeam(
    ICurrentPrincipalAccessor principalAccessor,
    ITenantInvitationStore store,
    TimeProvider timeProvider)
{
    public Task<TenantTeamMutationStatus> UpdateAsync(Guid membershipId, string role,
        IReadOnlyList<Guid> clinicUnitIds, CancellationToken cancellationToken) =>
        ExecuteAsync(membershipId, role, clinicUnitIds, false, cancellationToken);

    public Task<TenantTeamMutationStatus> DeactivateAsync(Guid membershipId, CancellationToken cancellationToken) =>
        ExecuteAsync(membershipId, null, [], true, cancellationToken);

    private async Task<TenantTeamMutationStatus> ExecuteAsync(Guid membershipId, string? roleName,
        IReadOnlyList<Guid> clinicUnitIds, bool deactivate, CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId) return TenantTeamMutationStatus.Unauthenticated;
        if (membershipId == Guid.Empty) return TenantTeamMutationStatus.InvalidInput;
        if (!deactivate && (!Enum.TryParse<TenantMembershipRole>(roleName, out var parsedRole) ||
            !Enum.IsDefined(parsedRole) || parsedRole.ToString() != roleName || clinicUnitIds.Count == 0 ||
            clinicUnitIds.Any(id => id == Guid.Empty) || clinicUnitIds.Distinct().Count() != clinicUnitIds.Count))
            return TenantTeamMutationStatus.InvalidInput;

        await using var transaction = await store.BeginAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(now) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
            return TenantTeamMutationStatus.Unauthenticated;
        var actor = await transaction.FindMembershipAsync(tenantId, principal.UserId, cancellationToken);
        if (session.ActiveTenantId != tenantId || actor?.Status != MembershipStatus.Active ||
            actor.Role != TenantMembershipRole.ClinicAdministrator) return TenantTeamMutationStatus.Forbidden;

        var members = await transaction.ListMembersAsync(tenantId, cancellationToken);
        var targetData = members.SingleOrDefault(item => item.MembershipId == membershipId);
        if (targetData is null) return TenantTeamMutationStatus.NotFound;
        var target = await transaction.FindMembershipAsync(tenantId, targetData.UserId, cancellationToken);
        if (target is null) return TenantTeamMutationStatus.NotFound;

        if (deactivate)
        {
            if (target.Status == MembershipStatus.Revoked) return TenantTeamMutationStatus.InvalidState;
            if (target.UserId == principal.UserId) return TenantTeamMutationStatus.CannotDeactivateSelf;
            if (target.Role == TenantMembershipRole.ClinicAdministrator && target.Status == MembershipStatus.Active &&
                await transaction.CountActiveAdministratorsAsync(tenantId, cancellationToken) <= 1)
                return TenantTeamMutationStatus.LastAdministrator;
            target.Deactivate();
            await transaction.RevokeInvitationTokensAsync(target.Id, now, cancellationToken);
            await transaction.RevokeTenantSessionsAsync(tenantId, target.UserId, now, cancellationToken);
        }
        else
        {
            var role = Enum.Parse<TenantMembershipRole>(roleName!);
            if (target.Status is not (MembershipStatus.Active or MembershipStatus.Invited))
                return TenantTeamMutationStatus.InvalidState;
            if (!await transaction.AreUnitsActiveAsync(tenantId, clinicUnitIds, cancellationToken))
                return TenantTeamMutationStatus.UnitNotFound;
            var allowed = (await transaction.GetActiveUnitIdsAsync(tenantId, actor.Id, cancellationToken)).ToHashSet();
            if (clinicUnitIds.Any(id => !allowed.Contains(id))) return TenantTeamMutationStatus.Forbidden;
            if (target.UserId == principal.UserId && target.Role == TenantMembershipRole.ClinicAdministrator &&
                role != TenantMembershipRole.ClinicAdministrator &&
                await transaction.CountActiveAdministratorsAsync(tenantId, cancellationToken) <= 1)
                return TenantTeamMutationStatus.LastAdministrator;
            target.ChangeRole(role);
            await transaction.SetUnitMembershipsAsync(tenantId, target.Id, clinicUnitIds, now, cancellationToken);
            await transaction.RevokeTenantSessionsAsync(tenantId, target.UserId, now, cancellationToken);
        }

        await transaction.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TenantTeamMutationStatus.Success;
    }
}

public enum TenantTeamMutationStatus
{
    Success = 1, Unauthenticated = 2, Forbidden = 3, InvalidInput = 4, NotFound = 5,
    UnitNotFound = 6, InvalidState = 7, CannotDeactivateSelf = 8, LastAdministrator = 9,
}
