using System.Security.Cryptography;
using System.Text;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.PasswordRecovery;

public sealed class ConfirmPasswordRecovery(
    IPasswordRecoveryStore store,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    IAuthenticationAudit audit)
{
    public async Task<bool> ExecuteAsync(ConfirmPasswordRecoveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!tokenGenerator.TryHash(command.Token, out var hash) ||
            string.IsNullOrEmpty(command.Password) || command.Password.Length is < 12 or > 128 ||
            command.PasswordConfirmation is null || command.PasswordConfirmation.Length > 128 ||
            !command.Password.Any(char.IsLetter) || !command.Password.Any(char.IsDigit) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(command.Password), Encoding.UTF8.GetBytes(command.PasswordConfirmation)))
        {
            return false;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var token = await transaction.FindTokenAsync(hash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (token is null || token.Purpose != AccountActionTokenPurpose.PasswordRecovery ||
            !token.IsUsableAt(now) || !tokenGenerator.Matches(command.Token, token.SecretHash))
        {
            return false;
        }

        var user = await transaction.FindUserByIdAsync(token.UserId, cancellationToken);
        var credential = await transaction.FindCredentialAsync(token.UserId, cancellationToken);
        credential?.UnlockIfExpired(now);
        if (user is null || credential?.Status != UserCredentialStatus.Active ||
            credential.PasswordChangedAt > token.CreatedAt)
        {
            return false;
        }

        var passwordHash = passwordHasher.Hash(user, command.Password);
        now = timeProvider.GetUtcNow();
        if (!token.IsUsableAt(now))
        {
            return false;
        }

        credential.ChangePassword(passwordHash, now);
        token.Consume(now);
        await transaction.RevokeSessionsAsync(user.Id, now, cancellationToken);
        await transaction.RevokeRecoveryTokensAsync(user.Id, now, token.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.PasswordChanged, now);
        return true;
    }
}

public sealed record ConfirmPasswordRecoveryCommand(string Token, string Password, string PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}
