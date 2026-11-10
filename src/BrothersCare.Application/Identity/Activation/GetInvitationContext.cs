using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Activation;

public sealed class GetInvitationContext(
    IAccountActivationStore store,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider)
{
    public async Task<InvitationContext?> ExecuteAsync(string presentedToken, CancellationToken cancellationToken)
    {
        if (!tokenGenerator.TryHash(presentedToken, out var hash)) return null;
        await using var transaction = await store.BeginAsync(cancellationToken);
        var token = await transaction.FindTokenAsync(hash, cancellationToken);
        if (token is null || token.Purpose is not (AccountActionTokenPurpose.InvitationAcceptance or AccountActionTokenPurpose.AccountActivation) ||
            !token.IsUsableAt(timeProvider.GetUtcNow()) ||
            !tokenGenerator.Matches(presentedToken, token.SecretHash)) return null;
        var user = await transaction.FindUserAsync(token.UserId, cancellationToken);
        if (user is null) return null;
        if (token.Purpose == AccountActionTokenPurpose.AccountActivation)
            return new(user.DisplayName, user.Email, user.Cpf is not null, Guid.Empty,
                "Brothers Care", "SuperAdministrator", [], token.ExpiresAt);
        if (token.TenantMembershipId is not { } membershipId) return null;
        var membership = await transaction.FindInvitationAsync(membershipId, token.UserId, cancellationToken);
        if (membership?.Status != MembershipStatus.Invited) return null;
        var tenant = await transaction.FindTenantAsync(membership.TenantId, cancellationToken);
        if (tenant?.Status != TenantStatus.Active) return null;
        var units = await transaction.ListMembershipUnitsAsync(tenant.Id, membership.Id, cancellationToken);
        return new(user.DisplayName, user.Email, user.Cpf is not null, tenant.Id, tenant.Name,
            membership.Role.ToString(), units, token.ExpiresAt);
    }
}

public sealed record InvitationContext(string DisplayName, string Email, bool HasCpf,
    Guid TenantId, string TenantName, string Role, IReadOnlyList<ActivationClinicUnit> Units, DateTimeOffset ExpiresAt);
