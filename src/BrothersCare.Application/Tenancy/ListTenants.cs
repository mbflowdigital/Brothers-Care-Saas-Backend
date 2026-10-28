namespace BrothersCare.Application.Tenancy;

public interface ITenantListStore
{
    Task<IReadOnlyList<TenantListItem>> ListAsync(string? search, CancellationToken cancellationToken);
}

public sealed record TenantListItem(Guid Id, string Name, string Status);

public sealed class ListTenants(ITenantListStore store)
{
    public Task<IReadOnlyList<TenantListItem>> ExecuteAsync(string? search, CancellationToken cancellationToken) =>
        store.ListAsync(search, cancellationToken);
}
