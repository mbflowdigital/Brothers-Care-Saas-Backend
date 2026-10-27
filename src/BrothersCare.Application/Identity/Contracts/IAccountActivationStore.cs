using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Contracts;

public interface IAccountActivationStore
{
    Task<IAccountActivationTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IAccountActivationTransaction : IAsyncDisposable
{
    Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken);
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<TenantMembership?> FindInvitationAsync(Guid membershipId, Guid userId, CancellationToken cancellationToken);
    Task<bool> IsTenantActiveAsync(Guid tenantId, CancellationToken cancellationToken);
    void AddCredential(UserCredential credential);
    Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
