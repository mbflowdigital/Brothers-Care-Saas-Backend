namespace BrothersCare.Application.Tenancy;

public interface ITenantListStore
{
    Task<IReadOnlyList<TenantListItem>> ListAsync(string? search, CancellationToken cancellationToken);

    Task<TenantDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record TenantListItem(Guid Id, string Name, string Status, string FirstAdministratorStatus);

public sealed record TenantDetails(
    Guid Id,
    string Name,
    string Status,
    TenantAdministratorDetails? FirstAdministrator);

public sealed record TenantAdministratorDetails(
    Guid MembershipId,
    string DisplayName,
    string Email,
    string Status);

public sealed class ListTenants(ITenantListStore store)
{
    public Task<IReadOnlyList<TenantListItem>> ExecuteAsync(string? search, CancellationToken cancellationToken) =>
        store.ListAsync(search, cancellationToken);
}

public sealed class GetTenant(ITenantListStore store)
{
    public Task<TenantDetails?> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken) =>
        tenantId == Guid.Empty
            ? Task.FromResult<TenantDetails?>(null)
            : store.GetAsync(tenantId, cancellationToken);
}
