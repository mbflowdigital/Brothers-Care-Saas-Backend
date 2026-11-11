using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Sessions;

public sealed class LogoutUser(
    IAuthenticationStore store,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    IAuthenticationAudit audit)
{
    public async Task ExecuteAsync(string? cookieValue, CancellationToken cancellationToken)
    {
        if (!SessionToken.TryParse(cookieValue, out var token) || !tokenGenerator.TryHash(token.Secret, out _))
        {
            return;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(token.SessionId, cancellationToken);
        if (session is null || !tokenGenerator.Matches(token.Secret, session.SecretHash) || session.RevokedAt is not null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (!session.TryExpire(now))
        {
            session.Revoke(now, SessionRevocationReason.Logout);
        }

        await transaction.CommitAsync(cancellationToken);
        if (session.RevocationReason == SessionRevocationReason.Logout)
        {
            audit.Record(AuthenticationAuditEvent.Logout, now);
        }
    }
}
