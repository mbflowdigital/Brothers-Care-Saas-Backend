using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantInvitationStore(BrothersCareDbContext dbContext) : ITenantInvitationStore
{
    public async Task<ITenantInvitationTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new InvitationTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class InvitationTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : ITenantInvitationTransaction
    {
        public async Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var session = await context.UserSessions.SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
            if (session is not null)
            {
                await context.Entry(session).ReloadAsync(cancellationToken);
            }

            return session;
        }

        public Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(item => item.UserId == userId && item.Status == UserCredentialStatus.Active, cancellationToken);

        public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        public async Task<TenantMembership?> FindMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
        {
            var membership = await context.TenantMemberships.SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.UserId == userId, cancellationToken);
            if (membership is not null)
            {
                await context.Entry(membership).ReloadAsync(cancellationToken);
            }

            return membership;
        }

        public async Task<IReadOnlyList<Guid>> GetActiveUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
            await (from link in context.ClinicUnitMemberships
                   join unit in context.ClinicUnits
                       on new { link.TenantId, UnitId = link.ClinicUnitId } equals new { unit.TenantId, UnitId = unit.Id }
                   where link.TenantId == tenantId && link.TenantMembershipId == membershipId && unit.IsActive
                   select unit.Id).ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<Guid>> GetMembershipUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
            await context.ClinicUnitMemberships.Where(item => item.TenantId == tenantId && item.TenantMembershipId == membershipId)
                .Select(item => item.ClinicUnitId).ToListAsync(cancellationToken);

        public async Task<bool> AreUnitsActiveAsync(Guid tenantId, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken) =>
            await context.ClinicUnits.CountAsync(item => item.TenantId == tenantId && item.IsActive && unitIds.Contains(item.Id), cancellationToken)
                == unitIds.Count;

        public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);

        public Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(item => item.UserId == userId, cancellationToken);

        public void AddUser(User user) => context.Users.Add(user);
        public void AddCredential(UserCredential credential) => context.UserCredentials.Add(credential);
        public void AddMembership(TenantMembership membership) => context.TenantMemberships.Add(membership);
        public void AddToken(AccountActionToken token) => context.AccountActionTokens.Add(token);

        public async Task SetUnitMembershipsAsync(Guid tenantId, Guid membershipId, IReadOnlyList<Guid> unitIds,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            var existing = await context.ClinicUnitMemberships
                .Where(item => item.TenantId == tenantId && item.TenantMembershipId == membershipId).ToListAsync(cancellationToken);
            context.ClinicUnitMemberships.RemoveRange(existing.Where(item => !unitIds.Contains(item.ClinicUnitId)));
            foreach (var unitId in unitIds.Where(id => existing.All(item => item.ClinicUnitId != id)))
            {
                context.ClinicUnitMemberships.Add(new ClinicUnitMembership(Guid.NewGuid(), tenantId, unitId, membershipId, now));
            }
        }

        public async Task RevokePreviousInvitationsAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var tokens = await context.AccountActionTokens.Where(item => item.TenantMembershipId == membershipId &&
                item.Purpose == AccountActionTokenPurpose.InvitationAcceptance && item.RevokedAt == null && item.ConsumedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        public async Task RevokeTenantSessionsAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions.Where(item => item.UserId == userId &&
                item.ActiveTenantId == tenantId && item.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                if (!session.TryExpire(now))
                {
                    session.Revoke(now, SessionRevocationReason.SecurityIncident);
                }
            }
        }

        public async Task SaveAsync(CancellationToken cancellationToken) => await context.SaveChangesAsync(cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
