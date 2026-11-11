using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlAuthenticationStore(BrothersCareDbContext dbContext) : IAuthenticationStore
{
    public async Task<IAuthenticationTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new AuthenticationTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class AuthenticationTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IAuthenticationTransaction
    {
        public Task<User?> FindUserByCpfAsync(string cpf, CancellationToken cancellationToken) =>
            context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Cpf == cpf, cancellationToken);

        public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        public async Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await context.UserCredentials.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);
        }

        public Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            context.UserSessions.SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);

        public Task<SessionAuthenticationState?> FindSessionStateAsync(Guid sessionId, CancellationToken cancellationToken) =>
            (from session in context.UserSessions
             join credential in context.UserCredentials on session.UserId equals credential.UserId
             where session.Id == sessionId
             select new SessionAuthenticationState(session, credential.Status))
            .SingleOrDefaultAsync(cancellationToken);

        public async Task<TenantMembership?> FindSelectableMembershipAsync(
            Guid userId, Guid tenantId, CancellationToken cancellationToken)
        {
            var membership = await context.TenantMemberships.SingleOrDefaultAsync(
                item => item.UserId == userId && item.TenantId == tenantId && item.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null)
            {
                return null;
            }

            var tenantIsActive = await context.Tenants.AnyAsync(
                item => item.Id == tenantId && item.Status == TenantStatus.Active,
                cancellationToken);
            return tenantIsActive ? membership : null;
        }

        public async Task<IReadOnlyList<Guid>> GetAuthorizedClinicUnitIdsAsync(
            Guid userId, Guid tenantId, CancellationToken cancellationToken) => await (
                from membership in context.TenantMemberships.AsNoTracking()
                join unitMembership in context.ClinicUnitMemberships.AsNoTracking()
                    on new { membership.TenantId, MembershipId = membership.Id }
                    equals new { unitMembership.TenantId, MembershipId = unitMembership.TenantMembershipId }
                join unit in context.ClinicUnits.AsNoTracking()
                    on new { unitMembership.TenantId, Id = unitMembership.ClinicUnitId } equals new { unit.TenantId, unit.Id }
                join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                where membership.UserId == userId && membership.TenantId == tenantId &&
                    membership.Status == MembershipStatus.Active && unit.IsActive && tenant.Status == TenantStatus.Active
                select unit.Id).Distinct().ToListAsync(cancellationToken);

        public void AddSession(UserSession session) => context.UserSessions.Add(session);

        public async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions.Where(item => item.UserId == userId && item.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now, reason);
            }
        }

        public async Task<SessionSummary?> GetSummaryAsync(UserSession session, CancellationToken cancellationToken)
        {
            var identity = await (
                from user in context.Users.AsNoTracking()
                where user.Id == session.UserId
                join platformAdministrator in context.PlatformAdministrators.AsNoTracking()
                        .Where(item => item.Status == PlatformAdministratorStatus.Active)
                    on user.Id equals platformAdministrator.UserId into platformAdministrators
                from platformAdministrator in platformAdministrators.DefaultIfEmpty()
                select new
                {
                    User = new SessionUser(user.Id, user.DisplayName, user.Email),
                    AdministratorRole = platformAdministrator == null ? null : platformAdministrator.Role.ToString(),
                }).SingleOrDefaultAsync(cancellationToken);
            if (identity is null)
            {
                return null;
            }
            var memberships = identity.AdministratorRole is not null
                ? new List<SessionMembership>()
                : await (
                from membership in context.TenantMemberships.AsNoTracking()
                join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                where membership.UserId == identity.User.Id && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                orderby tenant.Name
                select new SessionMembership(membership.Id, tenant.Id, tenant.Name, membership.Role.ToString()))
                .ToListAsync(cancellationToken);

            SessionTenant? activeTenant = null;
            if (session.ActiveTenantId is { } tenantId)
            {
                var membership = memberships.SingleOrDefault(item => item.TenantId == tenantId);
                if (membership is null)
                {
                    return null;
                }

                activeTenant = new SessionTenant(tenantId, membership.TenantName);
            }

            var administrator = identity.AdministratorRole is null
                ? null
                : new SessionPlatformAdministrator(identity.AdministratorRole);
            return new SessionSummary(identity.User, administrator, memberships, activeTenant, session.IdleExpiresAt);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
