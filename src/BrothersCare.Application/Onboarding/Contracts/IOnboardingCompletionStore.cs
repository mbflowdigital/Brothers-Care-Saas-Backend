using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Onboarding.Contracts;

public interface IOnboardingCompletionStore
{
    Task<IOnboardingCompletionTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IOnboardingCompletionTransaction : IAsyncDisposable
{
    Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken);
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<User?> FindUserByCpfAsync(string cpf, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantMembership?> FindMembershipAsync(Guid membershipId, Guid userId, CancellationToken cancellationToken);
    Task<bool> HasClinicUnitAsync(Guid tenantId, CancellationToken cancellationToken);
    void AddClinicUnit(ClinicUnit clinicUnit);
    void AddClinicUnitMembership(ClinicUnitMembership membership);
    Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
