using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Support;

// In-memory port double: no database, SQL locking, rollback or concurrency guarantees are simulated.
internal sealed class IdentityMemoryStore : IAuthenticationStore, IAuthenticationTransaction,
    IAccountActivationStore, IAccountActivationTransaction, IPasswordRecoveryStore, IPasswordRecoveryTransaction,
    IPlatformAdministratorStore, IPlatformAdministratorTransaction, ITenantInvitationStore, ITenantInvitationTransaction,
    ITenantProvisioningStore, ITenantProvisioningTransaction, ITenantInvitationPlanUsageStore
{
    public List<User> Users { get; } = [];
    public List<UserCredential> Credentials { get; } = [];
    public List<UserSession> Sessions { get; } = [];
    public List<AccountActionToken> Tokens { get; } = [];
    public List<PlatformAdministrator> Administrators { get; } = [];
    public List<Tenant> Tenants { get; } = [];
    public List<ClinicUnit> Units { get; } = [];
    public List<TenantMembership> Memberships { get; } = [];
    public List<ClinicUnitMembership> UnitMemberships { get; } = [];
    public int Commits { get; private set; }
    public string? LastNormalizedEmail { get; private set; }

    Task<IAuthenticationTransaction> IAuthenticationStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IAuthenticationTransaction>(this);
    Task<IAccountActivationTransaction> IAccountActivationStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IAccountActivationTransaction>(this);
    Task<IPasswordRecoveryTransaction> IPasswordRecoveryStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IPasswordRecoveryTransaction>(this);
    Task<IPlatformAdministratorTransaction> IPlatformAdministratorStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IPlatformAdministratorTransaction>(this);
    Task<ITenantInvitationTransaction> ITenantInvitationStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<ITenantInvitationTransaction>(this);
    Task<ITenantProvisioningTransaction> ITenantProvisioningStore.BeginAsync(CancellationToken cancellationToken) => Task.FromResult<ITenantProvisioningTransaction>(this);

    public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        LastNormalizedEmail = normalizedEmail;
        return Task.FromResult(Users.SingleOrDefault(item => item.NormalizedEmail == normalizedEmail));
    }

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) => FindUserByIdAsync(userId, cancellationToken);
    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Users.SingleOrDefault(item => item.Id == userId));
    public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Credentials.SingleOrDefault(item => item.UserId == userId));
    public Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult(Sessions.SingleOrDefault(item => item.Id == sessionId));
    public Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken) => Task.FromResult(Tokens.SingleOrDefault(item => item.SecretHash.SequenceEqual(secretHash)));
    public Task<PlatformAdministrator?> FindAdministratorAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Administrators.SingleOrDefault(item => item.UserId == userId));
    public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Tenants.SingleOrDefault(item => item.Id == tenantId));
    public Task<TenantMembership?> FindMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(Memberships.SingleOrDefault(item => item.TenantId == tenantId && item.UserId == userId));
    public Task<TenantMembership?> FindInvitationAsync(Guid membershipId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(Memberships.SingleOrDefault(item => item.Id == membershipId && item.UserId == userId));
    public Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Tenants.Any(item => item.Id == tenantId && item.Status == TenantStatus.Active));
    public Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Credentials.Any(item => item.UserId == userId));
    public Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Credentials.Any(item => item.UserId == userId && item.Status == UserCredentialStatus.Active));
    public Task<bool> IsSessionActiveAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Sessions.Any(item => item.Id == sessionId && item.UserId == userId && item.IsValidAt(now)));
    public Task<bool> IsActiveSuperAdministratorAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Administrators.Any(item => item.UserId == userId && item.Status == PlatformAdministratorStatus.Active && item.Role == PlatformAdministratorRole.SuperAdministrator));

    public Task<TenantMembership?> FindSelectableMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Memberships.SingleOrDefault(item => item.UserId == userId && item.TenantId == tenantId && item.Status == MembershipStatus.Active && Tenants.Any(tenant => tenant.Id == tenantId && tenant.Status == TenantStatus.Active)));

    public Task<SessionSummary?> GetSummaryAsync(UserSession session, CancellationToken cancellationToken)
    {
        var user = Users.SingleOrDefault(item => item.Id == session.UserId);
        if (user is null)
        {
            return Task.FromResult<SessionSummary?>(null);
        }
        var memberships = (from member in Memberships
                           join tenant in Tenants on member.TenantId equals tenant.Id
                           where member.UserId == user.Id && member.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                           select new SessionMembership(member.Id, tenant.Id, tenant.Name, member.Role.ToString())).ToArray();
        var selected = memberships.SingleOrDefault(item => item.TenantId == session.ActiveTenantId);
        if (session.ActiveTenantId is not null && selected is null)
        {
            return Task.FromResult<SessionSummary?>(null);
        }
        var administrator = Administrators.SingleOrDefault(item => item.UserId == user.Id && item.Status == PlatformAdministratorStatus.Active);
        return Task.FromResult<SessionSummary?>(new SessionSummary(new SessionUser(user.Id, user.DisplayName, user.Email),
            administrator is null ? null : new SessionPlatformAdministrator(administrator.Role.ToString()), memberships,
            selected is null ? null : new SessionTenant(selected.TenantId, selected.TenantName), session.IdleExpiresAt));
    }

    public async Task<IReadOnlyList<Guid>> GetAuthorizedClinicUnitIdsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var membership = await FindSelectableMembershipAsync(userId, tenantId, cancellationToken);
        return membership is null ? [] : await GetActiveUnitIdsAsync(tenantId, membership.Id, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetActiveUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>((from link in UnitMemberships
                                             join unit in Units on link.ClinicUnitId equals unit.Id
                                             where link.TenantId == tenantId && link.TenantMembershipId == membershipId && unit.TenantId == tenantId && unit.IsActive
                                             select unit.Id).ToArray());
    public Task<IReadOnlyList<Guid>> GetMembershipUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(UnitMemberships.Where(item => item.TenantId == tenantId && item.TenantMembershipId == membershipId).Select(item => item.ClinicUnitId).ToArray());
    public Task<bool> AreUnitsActiveAsync(Guid tenantId, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken) => Task.FromResult(unitIds.All(id => Units.Any(item => item.Id == id && item.TenantId == tenantId && item.IsActive)));
    public Task<MembershipStatus?> FindMembershipStatusAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) => Task.FromResult(Memberships.Where(item => item.TenantId == tenantId && item.Id == membershipId).Select(item => (MembershipStatus?)item.Status).SingleOrDefault());
    public Task<int> CountMembershipsAsync(Guid tenantId, IReadOnlyList<MembershipStatus> statuses, CancellationToken cancellationToken) => Task.FromResult(Memberships.Count(item => item.TenantId == tenantId && statuses.Contains(item.Status)));

    public void AddUser(User user) => Users.Add(user);
    public void AddCredential(UserCredential credential) => Credentials.Add(credential);
    public void AddAdministrator(PlatformAdministrator administrator) => Administrators.Add(administrator);
    public void AddSession(UserSession session) => Sessions.Add(session);
    public void AddToken(AccountActionToken token) => Tokens.Add(token);
    public void AddInvitationToken(AccountActionToken token) => Tokens.Add(token);
    public void AddTenant(Tenant tenant) => Tenants.Add(tenant);
    public void AddClinicUnit(ClinicUnit unit) => Units.Add(unit);
    public void AddMembership(TenantMembership membership) => Memberships.Add(membership);
    public void AddUnitMembership(ClinicUnitMembership membership) => UnitMemberships.Add(membership);

    public Task SetUnitMembershipsAsync(Guid tenantId, Guid membershipId, IReadOnlyList<Guid> unitIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        UnitMemberships.RemoveAll(item => item.TenantId == tenantId && item.TenantMembershipId == membershipId);
        UnitMemberships.AddRange(unitIds.Select(id => new ClinicUnitMembership(Guid.NewGuid(), tenantId, id, membershipId, now)));
        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) => RevokeSessionsAsync(userId, now, SessionRevocationReason.PasswordChanged, cancellationToken);
    public Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        foreach (var session in Sessions.Where(item => item.UserId == userId && item.RevokedAt is null))
        {
            session.Revoke(now, reason);
        }
        return Task.CompletedTask;
    }

    public Task RevokeTenantSessionsAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var session in Sessions.Where(item => item.UserId == userId && item.ActiveTenantId == tenantId && item.RevokedAt is null))
        {
            session.Revoke(now, SessionRevocationReason.SecurityIncident);
        }
        return Task.CompletedTask;
    }

    public Task RevokePreviousInvitationsAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeTokensAsync(item => item.TenantMembershipId == membershipId && item.Purpose == AccountActionTokenPurpose.InvitationAcceptance, now);
    public Task RevokeActivationTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeTokensAsync(item => item.UserId == userId && item.Purpose == AccountActionTokenPurpose.AccountActivation, now);
    public Task RevokeRecoveryTokensAsync(Guid userId, DateTimeOffset now, Guid? exceptTokenId, CancellationToken cancellationToken) =>
        RevokeTokensAsync(item => item.UserId == userId && item.Purpose == AccountActionTokenPurpose.PasswordRecovery && item.Id != exceptTokenId, now);

    private Task RevokeTokensAsync(Func<AccountActionToken, bool> predicate, DateTimeOffset now)
    {
        foreach (var token in Tokens.Where(item => item.ConsumedAt is null && item.RevokedAt is null).Where(predicate))
        {
            token.Revoke(now);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
