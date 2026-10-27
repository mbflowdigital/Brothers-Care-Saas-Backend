using System.Data;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantProvisioningStore(BrothersCareDbContext dbContext) : ITenantProvisioningStore
{
    public async Task<ITenantProvisioningTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        return new ProvisioningTransaction(dbContext, transaction);
    }

    private sealed class ProvisioningTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : ITenantProvisioningTransaction
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

        public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);

        public Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(item => item.UserId == userId, cancellationToken);

        public void AddUser(User user) => context.Users.Add(user);
        public void AddCredential(UserCredential credential) => context.UserCredentials.Add(credential);
        public void AddTenant(Tenant tenant) => context.Tenants.Add(tenant);
        public void AddClinicUnit(ClinicUnit unit) => context.ClinicUnits.Add(unit);
        public void AddMembership(TenantMembership membership) => context.TenantMemberships.Add(membership);
        public void AddUnitMembership(ClinicUnitMembership membership) => context.ClinicUnitMemberships.Add(membership);
        public void AddInvitationToken(AccountActionToken token) => context.AccountActionTokens.Add(token);

        public async Task SaveAsync(CancellationToken cancellationToken) =>
            await context.SaveChangesAsync(cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
