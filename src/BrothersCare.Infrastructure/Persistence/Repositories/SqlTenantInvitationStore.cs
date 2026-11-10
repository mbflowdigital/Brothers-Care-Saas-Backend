using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Care;
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

        public async Task<IReadOnlyList<TenantTeamMember>> ListMembersAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var rows = await (from membership in context.TenantMemberships.AsNoTracking()
                              join user in context.Users.AsNoTracking() on membership.UserId equals user.Id
                              where membership.TenantId == tenantId
                              orderby membership.Status, user.DisplayName
                              select new { membership, user }).ToListAsync(cancellationToken);
            var membershipIds = rows.Select(item => item.membership.Id).ToArray();
            var professionalProfiles = await context.Professionals.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.TenantMembershipId != null &&
                    membershipIds.Contains(item.TenantMembershipId.Value))
                .ToDictionaryAsync(item => item.TenantMembershipId!.Value, cancellationToken);
            var units = await (from link in context.ClinicUnitMemberships.AsNoTracking()
                               join unit in context.ClinicUnits.AsNoTracking()
                                   on new { link.TenantId, UnitId = link.ClinicUnitId } equals new { unit.TenantId, UnitId = unit.Id }
                               where link.TenantId == tenantId && membershipIds.Contains(link.TenantMembershipId)
                               select new { link.TenantMembershipId, unit.Id, unit.Name }).ToListAsync(cancellationToken);
            return rows.Select(item =>
            {
                professionalProfiles.TryGetValue(item.membership.Id, out var profile);
                return new TenantTeamMember(
                item.membership.Id, item.user.Id, item.user.DisplayName, item.user.Email,
                item.membership.Role.ToString(), item.membership.Status.ToString(), item.membership.CreatedAt,
                units.Where(unit => unit.TenantMembershipId == item.membership.Id)
                    .Select(unit => new TenantTeamUnit(unit.Id, unit.Name)).ToArray(),
                profile?.Status == ProfessionalStatus.Active,
                profile?.Specialty,
                profile?.CouncilType,
                profile?.RegistrationNumber);
            }).ToArray();
        }

        public Task<int> CountActiveAdministratorsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            context.TenantMemberships.CountAsync(item => item.TenantId == tenantId &&
                item.Status == MembershipStatus.Active && item.Role == TenantMembershipRole.ClinicAdministrator, cancellationToken);

        public async Task RevokeInvitationTokensAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var tokens = await context.AccountActionTokens.Where(item => item.TenantMembershipId == membershipId &&
                item.Purpose == AccountActionTokenPurpose.InvitationAcceptance && item.ConsumedAt == null && item.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens) token.Revoke(now);
        }

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

        public async Task SetProfessionalProfileAsync(Guid tenantId, Guid membershipId, string displayName,
            bool performsCare, string? specialty, string? councilType, string? registrationNumber,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            var profile = await context.Professionals.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId && item.TenantMembershipId == membershipId, cancellationToken);
            if (!performsCare)
            {
                profile?.Inactivate();
                return;
            }

            if (profile is null)
            {
                profile = new Professional(Guid.NewGuid(), tenantId, displayName, specialty!, registrationNumber!, now);
                context.Professionals.Add(profile);
            }

            profile.ConfigureForMembership(membershipId, displayName, specialty!, councilType!, registrationNumber!);
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
