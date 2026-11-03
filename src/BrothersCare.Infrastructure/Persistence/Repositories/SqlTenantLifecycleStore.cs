using System.Data;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantLifecycleStore(BrothersCareDbContext dbContext) : ITenantLifecycleStore
{
    public async Task<ITenantLifecycleTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new TenantLifecycleTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class TenantLifecycleTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : ITenantLifecycleTransaction
    {
        public Task<bool> IsSessionActiveAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            context.UserSessions.AnyAsync(item => item.Id == sessionId && item.UserId == userId && item.RevokedAt == null &&
                item.CreatedAt <= now && item.IdleExpiresAt > now && item.AbsoluteExpiresAt > now, cancellationToken);

        public Task<bool> IsActiveSuperAdministratorAsync(Guid userId, CancellationToken cancellationToken) =>
            context.PlatformAdministrators.AnyAsync(item => item.UserId == userId &&
                item.Role == PlatformAdministratorRole.SuperAdministrator &&
                item.Status == PlatformAdministratorStatus.Active &&
                context.UserCredentials.Any(credential => credential.UserId == userId &&
                    credential.Status == UserCredentialStatus.Active), cancellationToken);

        public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        public async Task RevokeActiveTenantSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions.Where(item => item.ActiveTenantId == tenantId && item.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now, SessionRevocationReason.TenantUnavailable);
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
