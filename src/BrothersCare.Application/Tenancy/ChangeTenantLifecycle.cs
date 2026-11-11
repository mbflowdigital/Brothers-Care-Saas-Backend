using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class ChangeTenantLifecycle(
    ICurrentPrincipalAccessor principalAccessor,
    ITenantLifecycleStore store,
    TimeProvider timeProvider)
{
    public async Task<TenantLifecycleResult> ExecuteAsync(
        Guid tenantId,
        TenantLifecycleAction action,
        CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal is null)
        {
            return new(TenantLifecycleStatus.Unauthenticated);
        }

        if (tenantId == Guid.Empty || !Enum.IsDefined(action))
        {
            return new(TenantLifecycleStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!await transaction.IsSessionActiveAsync(principal.SessionId, principal.UserId, now, cancellationToken))
        {
            return new(TenantLifecycleStatus.Unauthenticated);
        }

        if (!await transaction.IsActiveSuperAdministratorAsync(principal.UserId, cancellationToken))
        {
            return new(TenantLifecycleStatus.Forbidden);
        }

        var tenant = await transaction.FindTenantAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return new(TenantLifecycleStatus.NotFound);
        }

        try
        {
            switch (action)
            {
                case TenantLifecycleAction.Suspend:
                    tenant.Suspend();
                    await transaction.RevokeActiveTenantSessionsAsync(tenantId, now, cancellationToken);
                    break;
                case TenantLifecycleAction.Reactivate:
                    tenant.Reactivate();
                    break;
                case TenantLifecycleAction.Deactivate:
                    tenant.Deactivate();
                    await transaction.RevokeActiveTenantSessionsAsync(tenantId, now, cancellationToken);
                    break;
            }
        }
        catch (InvalidOperationException)
        {
            return new(TenantLifecycleStatus.InvalidState);
        }

        await transaction.CommitAsync(cancellationToken);
        return new(TenantLifecycleStatus.Changed, tenant.Status.ToString());
    }
}

public enum TenantLifecycleAction
{
    Suspend = 1,
    Reactivate = 2,
    Deactivate = 3,
}

public enum TenantLifecycleStatus
{
    Changed = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    NotFound = 4,
    InvalidInput = 5,
    InvalidState = 6,
}

public sealed record TenantLifecycleResult(TenantLifecycleStatus Status, string? TenantStatus = null);
