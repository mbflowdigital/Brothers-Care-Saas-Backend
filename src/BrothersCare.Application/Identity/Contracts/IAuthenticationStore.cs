using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Contracts;

public interface IAuthenticationStore
{
    Task<IAuthenticationTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IAuthenticationTransaction : IAsyncDisposable
{
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<TenantMembership?> FindSelectableMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetAuthorizedClinicUnitIdsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
    void AddSession(UserSession session);
    Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken);
    Task<SessionSummary?> GetSummaryAsync(UserSession session, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
