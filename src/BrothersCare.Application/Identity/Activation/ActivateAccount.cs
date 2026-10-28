using System.Security.Cryptography;
using System.Text;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Activation;

public sealed class ActivateAccount(
    IAccountActivationStore store,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider) : IAccountActivationService
{
    public async Task<AccountActivationResult> ActivateAsync(
        AccountActivationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!tokenGenerator.TryHash(request.Token, out var hash))
        {
            return AccountActivationResult.InvalidToken;
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 12 or > 128 ||
            request.PasswordConfirmation is null || request.PasswordConfirmation.Length > 128 ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(request.Password), Encoding.UTF8.GetBytes(request.PasswordConfirmation)))
        {
            return AccountActivationResult.InvalidPassword;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var token = await transaction.FindTokenAsync(hash, cancellationToken);
        if (token is null || !token.IsUsableAt(timeProvider.GetUtcNow()) ||
            !tokenGenerator.Matches(request.Token, token.SecretHash) ||
            token.Purpose is not (AccountActionTokenPurpose.AccountActivation or AccountActionTokenPurpose.InvitationAcceptance))
        {
            return AccountActivationResult.InvalidToken;
        }

        TenantMembership? membership = null;
        if (token.Purpose == AccountActionTokenPurpose.InvitationAcceptance)
        {
            if (token.TenantMembershipId is not { } membershipId)
            {
                return AccountActivationResult.InvalidToken;
            }

            membership = await transaction.FindInvitationAsync(membershipId, token.UserId, cancellationToken);
            if (membership?.Status != MembershipStatus.Invited ||
                !await transaction.IsTenantActiveAsync(membership.TenantId, cancellationToken))
            {
                return AccountActivationResult.InvalidToken;
            }
        }

        var user = await transaction.FindUserAsync(token.UserId, cancellationToken);
        if (user is null)
        {
            return AccountActivationResult.InvalidToken;
        }

        var credential = await transaction.FindCredentialAsync(user.Id, cancellationToken);
        if (credential?.Status is UserCredentialStatus.Disabled or UserCredentialStatus.Locked)
        {
            return AccountActivationResult.InvalidToken;
        }

        var needsPassword = credential is null || credential.Status == UserCredentialStatus.PendingActivation;
        if (!needsPassword)
        {
            // Accepting a new tenant invitation must not reset an existing global password.
            if (membership is null || credential!.Status != UserCredentialStatus.Active ||
                credential.PasswordHash is null || !passwordHasher.Verify(user, credential.PasswordHash, request.Password))
            {
                return AccountActivationResult.InvalidToken;
            }
        }
        else if (!request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
        {
            return AccountActivationResult.InvalidPassword;
        }

        var passwordHash = needsPassword ? passwordHasher.Hash(user, request.Password) : null;
        var now = timeProvider.GetUtcNow();
        if (!token.IsUsableAt(now))
        {
            return AccountActivationResult.InvalidToken;
        }

        if (needsPassword)
        {
            if (credential is null)
            {
                credential = new UserCredential(Guid.NewGuid(), user.Id, now);
                transaction.AddCredential(credential);
            }

            credential.Activate(passwordHash!, now);
            await transaction.RevokeSessionsAsync(user.Id, now, cancellationToken);
        }

        membership?.AcceptInvitation();
        token.Consume(now);
        await transaction.CommitAsync(cancellationToken);
        return AccountActivationResult.Activated;
    }
}
