using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Contracts;

public interface ITenantInvitationStore
{
    Task<ITenantInvitationTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface ITenantInvitationTransaction : IAsyncDisposable
{
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantMembership?> FindMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetActiveUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetMembershipUnitIdsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantTeamMember>> ListMembersAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<int> CountActiveAdministratorsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task RevokeInvitationTokensAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> AreUnitsActiveAsync(Guid tenantId, IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken);
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken);
    void AddUser(User user);
    void AddCredential(UserCredential credential);
    void AddMembership(TenantMembership membership);
    void AddToken(AccountActionToken token);
    Task SetUnitMembershipsAsync(Guid tenantId, Guid membershipId, IReadOnlyList<Guid> unitIds, DateTimeOffset now, CancellationToken cancellationToken);
    Task SetProfessionalProfileAsync(Guid tenantId, Guid membershipId, string displayName, bool performsCare,
        string? specialty, string? councilType, string? registrationNumber, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokePreviousInvitationsAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeTenantSessionsAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record TenantTeamMember(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TenantTeamUnit> Units,
    bool PerformsCare,
    string? Specialty,
    string? CouncilType,
    string? RegistrationNumber);

public sealed record TenantTeamUnit(Guid Id, string Name);
