using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ITenantProvisioningStore
{
    Task<ITenantProvisioningTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface ITenantProvisioningTransaction : IAsyncDisposable
{
    Task<bool> IsSessionActiveAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> IsActiveSuperAdministratorAsync(Guid userId, CancellationToken cancellationToken);
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken);
    void AddUser(User user);
    void AddCredential(UserCredential credential);
    void AddTenant(Tenant tenant);
    void AddClinicUnit(ClinicUnit unit);
    void AddMembership(TenantMembership membership);
    void AddUnitMembership(ClinicUnitMembership membership);
    void AddInvitationToken(AccountActionToken token);
    Task SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
