using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ICommercialOnboardingStore
{
    Task<ICommercialOnboardingTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface ICommercialOnboardingTransaction : IAsyncDisposable
{
    Task<Tenant?> FindTenantByCnpjAsync(string cnpj, CancellationToken cancellationToken);
    Task<CommercialOnboardingInvitation?> FindPendingInvitationAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> HasCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task RevokeOnboardingTokensAsync(Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken);
    void AddTenant(Tenant tenant);
    void AddUser(User user);
    void AddCredential(UserCredential credential);
    void AddMembership(TenantMembership membership);
    void AddToken(AccountActionToken token);
    Task SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record CommercialOnboardingInvitation(Tenant Tenant, TenantMembership Membership, User User);
