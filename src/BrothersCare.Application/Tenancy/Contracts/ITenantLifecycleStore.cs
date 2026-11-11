using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ITenantLifecycleStore
{
    Task<ITenantLifecycleTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface ITenantLifecycleTransaction : IAsyncDisposable
{
    Task<bool> IsSessionActiveAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> IsActiveSuperAdministratorAsync(Guid userId, CancellationToken cancellationToken);

    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task RevokeActiveTenantSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}
