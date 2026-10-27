using System.Security.Cryptography;
using System.Text;
using BrothersCare.Application.Identity;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using BrothersCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Identity;

public sealed class AccountActivationService(
    BrothersCareDbContext dbContext,
    TimeProvider timeProvider) : IAccountActivationService
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async Task<AccountActivationResult> ActivateAsync(
        AccountActivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidPassword(request.Password, request.PasswordConfirmation))
        {
            return AccountActivationResult.InvalidPassword;
        }

        if (!TryGetSecretHash(request.Token, out var secretHash))
        {
            return AccountActivationResult.InvalidToken;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var actionToken = await dbContext.AccountActionTokens.SingleOrDefaultAsync(
            item => item.SecretHash == secretHash,
            cancellationToken);

        if (actionToken is null ||
            !actionToken.IsUsableAt(now) ||
            actionToken.Purpose is not (AccountActionTokenPurpose.AccountActivation or AccountActionTokenPurpose.InvitationAcceptance))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountActivationResult.InvalidToken;
        }

        TenantMembership? membership = null;
        if (actionToken.Purpose is AccountActionTokenPurpose.InvitationAcceptance)
        {
            membership = await dbContext.TenantMemberships.SingleOrDefaultAsync(
                item => item.Id == actionToken.TenantMembershipId && item.UserId == actionToken.UserId,
                cancellationToken);

            if (membership is null || membership.Status is not MembershipStatus.Invited)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccountActivationResult.InvalidToken;
            }
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.Id == actionToken.UserId,
            cancellationToken);

        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountActivationResult.InvalidToken;
        }

        var credential = await dbContext.UserCredentials.SingleOrDefaultAsync(
            item => item.UserId == user.Id,
            cancellationToken);
        if (credential is null)
        {
            credential = new UserCredential(Guid.NewGuid(), user.Id, now);
            dbContext.UserCredentials.Add(credential);
        }

        credential.Activate(PasswordHasher.HashPassword(user, request.Password), now);
        membership?.AcceptInvitation();
        actionToken.Consume(now);

        var activeSessions = await dbContext.UserSessions
            .Where(item => item.UserId == user.Id && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.Revoke(now, SessionRevocationReason.PasswordChanged);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountActivationResult.Activated;
    }

    private static bool IsValidPassword(string password, string passwordConfirmation)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 12 or > 128 || passwordConfirmation is null)
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var confirmationBytes = Encoding.UTF8.GetBytes(passwordConfirmation);
        if (!CryptographicOperations.FixedTimeEquals(passwordBytes, confirmationBytes))
        {
            return false;
        }

        return password.Any(char.IsLetter) && password.Any(char.IsDigit);
    }

    private static bool TryGetSecretHash(string token, out byte[] secretHash)
    {
        secretHash = [];
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var secret = WebEncoders.Base64UrlDecode(token);
            if (secret.Length != 32)
            {
                return false;
            }

            secretHash = SHA256.HashData(secret);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
