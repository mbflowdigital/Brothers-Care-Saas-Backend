using System.Data;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlCommercialOnboardingStore(BrothersCareDbContext dbContext) : ICommercialOnboardingStore
{
    public async Task<ICommercialOnboardingTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        return new CommercialOnboardingTransaction(dbContext, transaction);
    }

    private sealed class CommercialOnboardingTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : ICommercialOnboardingTransaction
    {
        public Task<Tenant?> FindTenantByCnpjAsync(string cnpj, CancellationToken cancellationToken) =>
            context.Tenants.SingleOrDefaultAsync(item => item.Cnpj == cnpj, cancellationToken);

        public Task<CommercialOnboardingInvitation?> FindPendingInvitationAsync(
            Guid tenantId, CancellationToken cancellationToken) =>
            (from tenant in context.Tenants
             join membership in context.TenantMemberships on tenant.Id equals membership.TenantId
             join user in context.Users on membership.UserId equals user.Id
             where tenant.Id == tenantId
             select new CommercialOnboardingInvitation(tenant, membership, user))
            .SingleOrDefaultAsync(cancellationToken);

        public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);

        public Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(item => item.UserId == userId, cancellationToken);

        public async Task RevokeOnboardingTokensAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var tokens = await context.AccountActionTokens.Where(item =>
                item.TenantMembershipId == membershipId &&
                item.Purpose == AccountActionTokenPurpose.CommercialOnboarding &&
                item.ConsumedAt == null && item.RevokedAt == null).ToArrayAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        public void AddTenant(Tenant tenant) => context.Tenants.Add(tenant);

        public void AddUser(User user) => context.Users.Add(user);

        public void AddCredential(UserCredential credential) => context.UserCredentials.Add(credential);

        public void AddMembership(TenantMembership membership) => context.TenantMemberships.Add(membership);

        public void AddToken(AccountActionToken token) => context.AccountActionTokens.Add(token);

        public async Task SaveAsync(CancellationToken cancellationToken) =>
            await context.SaveChangesAsync(cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
