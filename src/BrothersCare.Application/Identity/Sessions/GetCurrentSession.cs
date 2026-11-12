using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Sessions;

public sealed class GetCurrentSession(
    IAuthenticationStore store,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    AuthenticationSettings settings)
{
    public async Task<SessionSummary?> ExecuteAsync(string? cookieValue, CancellationToken cancellationToken) =>
        (await AuthenticateAsync(cookieValue, cancellationToken))?.Summary;

    public async Task<AuthenticatedSession?> AuthenticateAsync(string? cookieValue, CancellationToken cancellationToken)
    {
        if (!SessionToken.TryParse(cookieValue, out var token))
        {
            return null;
        }

        var secret = token.Secret;
        if (!tokenGenerator.TryHash(secret, out _))
        {
            return null;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var state = await transaction.FindSessionStateAsync(token.SessionId, cancellationToken);
        var session = state?.Session;
        if (session is null || !tokenGenerator.Matches(secret, session.SecretHash))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            session.TryExpire(now);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (state!.CredentialStatus != UserCredentialStatus.Active)
        {
            session.Revoke(now, SessionRevocationReason.AccountDisabled);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var summary = await transaction.GetSummaryAsync(session, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var membership = summary.Memberships.SingleOrDefault(item => item.TenantId == session.ActiveTenantId);
        var unitIds = session.ActiveTenantId is { } tenantId
            ? await transaction.GetAuthorizedClinicUnitIdsAsync(session.UserId, tenantId, cancellationToken)
            : Array.Empty<Guid>();

        now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            session.TryExpire(now);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        // Avoid a database write on every authenticated request. Besides reducing latency,
        // this prevents concurrent browser requests from updating the same session row.
        if (now - session.LastSeenAt >= TimeSpan.FromMinutes(5))
        {
            session.RegisterActivity(now, settings.IdleTimeout);
            await transaction.CommitAsync(cancellationToken);
        }
        return new AuthenticatedSession(summary with { ExpiresAt = session.IdleExpiresAt },
            new CurrentPrincipal(session.UserId, session.Id, session.ActiveTenantId,
                summary.PlatformAdministrator?.Role, membership?.Id, membership?.Role, unitIds));
    }
}
