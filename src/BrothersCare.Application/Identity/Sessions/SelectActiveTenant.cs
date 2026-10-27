using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Sessions;

public sealed class SelectActiveTenant(
    ICurrentPrincipalAccessor principalAccessor,
    IAuthenticationStore store,
    TimeProvider timeProvider,
    AuthenticationSettings settings)
{
    public async Task<SelectActiveTenantResult> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal is null)
        {
            return new(TenantSelectionStatus.Unauthenticated);
        }

        if (tenantId == Guid.Empty)
        {
            return new(TenantSelectionStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId)
        {
            return new(TenantSelectionStatus.Unauthenticated);
        }

        if (!session.IsValidAt(timeProvider.GetUtcNow()))
        {
            if (session.TryExpire(timeProvider.GetUtcNow()))
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new(TenantSelectionStatus.Unauthenticated);
        }

        var credential = await transaction.FindCredentialAsync(session.UserId, cancellationToken);
        if (credential?.Status != UserCredentialStatus.Active)
        {
            return new(TenantSelectionStatus.Unauthenticated);
        }

        var membership = await transaction.FindSelectableMembershipAsync(session.UserId, tenantId, cancellationToken);
        if (membership is null)
        {
            return new(TenantSelectionStatus.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            if (session.TryExpire(now))
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new(TenantSelectionStatus.Unauthenticated);
        }

        session.SelectActiveTenant(membership, now);
        var summary = await transaction.GetSummaryAsync(session, cancellationToken);
        if (summary is null)
        {
            return new(TenantSelectionStatus.NotFound);
        }

        now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            if (session.TryExpire(now))
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new(TenantSelectionStatus.Unauthenticated);
        }

        session.RegisterActivity(now, settings.IdleTimeout);
        await transaction.CommitAsync(cancellationToken);
        return new(TenantSelectionStatus.Selected, summary with { ExpiresAt = session.IdleExpiresAt });
    }
}

public sealed record SelectActiveTenantResult(TenantSelectionStatus Status, SessionSummary? Session = null);

public enum TenantSelectionStatus
{
    Selected = 1,
    Unauthenticated = 2,
    InvalidInput = 3,
    NotFound = 4,
}
