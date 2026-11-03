using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class ResendCommercialOnboardingInvitation(
    ICommercialOnboardingStore store,
    ITokenGenerator tokenGenerator,
    ICommercialOnboardingSender? invitationSender,
    TimeProvider timeProvider)
{
    public async Task<CommercialOnboardingResendStatus> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return CommercialOnboardingResendStatus.NotFound;
        }

        if (invitationSender is null)
        {
            return CommercialOnboardingResendStatus.DeliveryUnavailable;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var invitation = await transaction.FindPendingInvitationAsync(tenantId, cancellationToken);
        if (invitation is null || invitation.Tenant.Status != TenantStatus.PendingOnboarding ||
            invitation.Membership.Status != MembershipStatus.Invited ||
            invitation.Membership.Role != TenantMembershipRole.ClinicAdministrator)
        {
            return CommercialOnboardingResendStatus.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        await transaction.RevokeOnboardingTokensAsync(invitation.Membership.Id, now, cancellationToken);
        var generatedToken = tokenGenerator.Generate();
        var expiresAt = now.AddMinutes(30);
        transaction.AddToken(new AccountActionToken(
            Guid.NewGuid(), invitation.User.Id, AccountActionTokenPurpose.CommercialOnboarding,
            generatedToken.SecretHash, now, expiresAt, invitation.Membership.Id));
        await transaction.SaveAsync(cancellationToken);
        await invitationSender.SendAsync(new InvitationDelivery(
            invitation.User.Email, invitation.User.DisplayName, invitation.Tenant.Name,
            generatedToken.Secret, expiresAt), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CommercialOnboardingResendStatus.Accepted;
    }
}

public enum CommercialOnboardingResendStatus
{
    Accepted = 1,
    NotFound = 2,
    DeliveryUnavailable = 3,
}
