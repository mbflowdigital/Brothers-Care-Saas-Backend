using System.Security.Cryptography;
using System.Text;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.PasswordChange;

public sealed class ChangePassword(
    ICurrentPrincipalAccessor principalAccessor,
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IAuthenticationAudit audit)
{
    public async Task<PasswordChangeStatus> ExecuteAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var principal = principalAccessor.Principal;
        if (principal is null)
        {
            return PasswordChangeStatus.Unauthenticated;
        }

        if (string.IsNullOrEmpty(command.CurrentPassword) || command.CurrentPassword.Length > 128 ||
            string.IsNullOrEmpty(command.NewPassword) || command.NewPassword.Length is < 12 or > 128 ||
            command.PasswordConfirmation is null || command.PasswordConfirmation.Length > 128 ||
            !command.NewPassword.Any(char.IsLetter) || !command.NewPassword.Any(char.IsDigit) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(command.NewPassword), Encoding.UTF8.GetBytes(command.PasswordConfirmation)))
        {
            return PasswordChangeStatus.InvalidInput;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.UserId != principal.UserId)
        {
            return PasswordChangeStatus.Unauthenticated;
        }

        if (!session.IsValidAt(now))
        {
            if (session.TryExpire(now))
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return PasswordChangeStatus.Unauthenticated;
        }

        var credential = await transaction.FindCredentialAsync(principal.UserId, cancellationToken);
        var user = await transaction.FindUserByIdAsync(principal.UserId, cancellationToken);
        if (user is null || credential?.Status != UserCredentialStatus.Active || credential.PasswordHash is null)
        {
            return PasswordChangeStatus.Unauthenticated;
        }

        if (!passwordHasher.Verify(user, credential.PasswordHash, command.CurrentPassword))
        {
            return PasswordChangeStatus.InvalidCurrentPassword;
        }

        var hash = passwordHasher.Hash(user, command.NewPassword);
        now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            if (session.TryExpire(now))
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return PasswordChangeStatus.Unauthenticated;
        }

        credential.ChangePassword(hash, now);
        await transaction.RevokeSessionsAsync(user.Id, now, SessionRevocationReason.PasswordChanged, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.PasswordChanged, now);
        return PasswordChangeStatus.Changed;
    }
}

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}

public enum PasswordChangeStatus
{
    Changed = 1,
    Unauthenticated = 2,
    InvalidInput = 3,
    InvalidCurrentPassword = 4,
}
