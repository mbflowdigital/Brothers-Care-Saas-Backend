using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Login;

public sealed class LoginUser(
    IAuthenticationStore store,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    AuthenticationSettings settings,
    IAuthenticationAudit audit)
{
    public async Task<LoginResult> ExecuteAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 320 ||
            string.IsNullOrEmpty(command.Password) || command.Password.Length > 128 ||
            !MailAddress.TryCreate(command.Email.Trim(), out var address) ||
            !string.Equals(address.Address, command.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new(LoginStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var user = await transaction.FindUserAsync(command.Email.Trim().ToUpperInvariant(), cancellationToken);
        var credential = user is null ? null : await transaction.FindCredentialAsync(user.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        credential?.UnlockIfExpired(now);
        if (user is null || credential?.Status != UserCredentialStatus.Active || credential.PasswordHash is null)
        {
            passwordHasher.VerifyDummy(command.Password);
            return new(LoginStatus.InvalidCredentials);
        }

        if (!passwordHasher.Verify(user, credential.PasswordHash, command.Password))
        {
            now = timeProvider.GetUtcNow();
            credential.RegisterFailedSignIn(now, settings.MaximumFailedAttempts, settings.LockoutDuration);
            if (credential.Status == UserCredentialStatus.Locked)
            {
                await transaction.RevokeSessionsAsync(user.Id, now, SessionRevocationReason.SecurityIncident, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            if (credential.Status == UserCredentialStatus.Locked)
            {
                audit.Record(AuthenticationAuditEvent.AccountLocked, now);
            }

            return new(LoginStatus.InvalidCredentials);
        }

        now = timeProvider.GetUtcNow();
        credential.RegisterSuccessfulSignIn(now);
        var generated = tokenGenerator.Generate();
        var absoluteExpiresAt = now.Add(settings.AbsoluteLifetime);
        var idleExpiresAt = now.Add(settings.IdleTimeout);
        var session = new UserSession(Guid.NewGuid(), user.Id, generated.SecretHash, now,
            idleExpiresAt < absoluteExpiresAt ? idleExpiresAt : absoluteExpiresAt, absoluteExpiresAt);
        transaction.AddSession(session);
        var summary = await transaction.GetSummaryAsync(session, cancellationToken);
        if (summary is null)
        {
            return new(LoginStatus.InvalidCredentials);
        }

        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.LoginSucceeded, now);
        return new(LoginStatus.Succeeded, summary, $"{session.Id:D}.{generated.Secret}", absoluteExpiresAt);
    }
}
