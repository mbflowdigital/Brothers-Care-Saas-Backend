using System.Data;
using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlOnboardingCompletionStore(BrothersCareDbContext dbContext) : IOnboardingCompletionStore
{
    public async Task<IOnboardingCompletionTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new OnboardingCompletionTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class OnboardingCompletionTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IOnboardingCompletionTransaction
    {
        public Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken) =>
            context.AccountActionTokens.SingleOrDefaultAsync(item => item.SecretHash == secretHash, cancellationToken);

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        public Task<User?> FindUserByCpfAsync(string cpf, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.Cpf == cpf, cancellationToken);

        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        public Task<TenantMembership?> FindMembershipAsync(
            Guid membershipId,
            Guid userId,
            CancellationToken cancellationToken) =>
            context.TenantMemberships.SingleOrDefaultAsync(
                item => item.Id == membershipId && item.UserId == userId,
                cancellationToken);

        public Task<bool> HasClinicUnitAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.ClinicUnits.AnyAsync(item => item.TenantId == tenantId, cancellationToken);

        public void AddClinicUnit(ClinicUnit clinicUnit) => context.ClinicUnits.Add(clinicUnit);

        public void AddClinicUnitMembership(ClinicUnitMembership membership) =>
            context.ClinicUnitMemberships.Add(membership);

        public async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions
                .Where(item => item.UserId == userId && item.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now, SessionRevocationReason.PasswordChanged);
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
