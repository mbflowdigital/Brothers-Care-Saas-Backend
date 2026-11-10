using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlAccountActivationStore(BrothersCareDbContext dbContext) : IAccountActivationStore
{
    public async Task<IAccountActivationTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new ActivationTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class ActivationTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IAccountActivationTransaction
    {
        public Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken) =>
            context.AccountActionTokens.SingleOrDefaultAsync(
                item => item.SecretHash == secretHash,
                cancellationToken);

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        public Task<User?> FindUserByCpfAsync(string cpf, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.Cpf == cpf, cancellationToken);

        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);

        public Task<TenantMembership?> FindInvitationAsync(
            Guid membershipId, Guid userId, CancellationToken cancellationToken) =>
            context.TenantMemberships.SingleOrDefaultAsync(
                item => item.Id == membershipId && item.UserId == userId,
                cancellationToken);

        public Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.Tenants.AnyAsync(item => item.Id == tenantId && item.Status == TenantStatus.Active, cancellationToken);

        public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.Tenants.AsNoTracking().SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        public async Task<IReadOnlyList<ActivationClinicUnit>> ListMembershipUnitsAsync(
            Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
            await (from link in context.ClinicUnitMemberships.AsNoTracking()
                   join unit in context.ClinicUnits.AsNoTracking()
                       on new { link.TenantId, UnitId = link.ClinicUnitId } equals new { unit.TenantId, UnitId = unit.Id }
                   where link.TenantId == tenantId && link.TenantMembershipId == membershipId && unit.IsActive
                   orderby unit.Name
                   select new ActivationClinicUnit(unit.Id, unit.Name)).ToListAsync(cancellationToken);

        public void AddCredential(UserCredential credential) => context.UserCredentials.Add(credential);

        public async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions.Where(item => item.UserId == userId && item.RevokedAt == null)
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
