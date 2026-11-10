using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Invitations;

public sealed class ListTenantTeam(
    ICurrentPrincipalAccessor principalAccessor,
    ITenantInvitationStore store,
    TimeProvider timeProvider)
{
    public async Task<TenantTeamResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId)
        {
            return new(TenantTeamStatus.Unauthenticated, []);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(timeProvider.GetUtcNow()) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return new(TenantTeamStatus.Unauthenticated, []);
        }

        var actor = await transaction.FindMembershipAsync(tenantId, principal.UserId, cancellationToken);
        if (session.ActiveTenantId != tenantId || actor?.Status != MembershipStatus.Active ||
            actor.Role != TenantMembershipRole.ClinicAdministrator)
        {
            return new(TenantTeamStatus.Forbidden, []);
        }

        var members = await transaction.ListMembersAsync(tenantId, cancellationToken);
        return new(TenantTeamStatus.Success, members);
    }
}

public sealed record TenantTeamResult(TenantTeamStatus Status, IReadOnlyList<TenantTeamMember> Items);

public enum TenantTeamStatus { Success = 1, Unauthenticated = 2, Forbidden = 3 }
