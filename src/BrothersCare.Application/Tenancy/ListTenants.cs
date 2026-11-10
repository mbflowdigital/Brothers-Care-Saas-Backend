namespace BrothersCare.Application.Tenancy;

public interface ITenantListStore
{
    Task<TenantPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<TenantDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record TenantListItem(
    Guid Id, string Name, string? Cnpj, string Status, string FirstAdministratorStatus,
    string? ResponsibleName, string? ResponsibleEmail);

public sealed record TenantPage(
    IReadOnlyList<TenantListItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    int PendingCount,
    int ActiveCount);

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
    public Task<TenantPage> ExecuteAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 100);
        return store.ListAsync(string.IsNullOrWhiteSpace(search) ? null : search.Trim(), page, pageSize, cancellationToken);
    }
}

public sealed class GetTenant(ITenantListStore store)
{
    public Task<TenantDetails?> ExecuteAsync(Guid tenantId, CancellationToken cancellationToken) =>
        tenantId == Guid.Empty
            ? Task.FromResult<TenantDetails?>(null)
            : store.GetAsync(tenantId, cancellationToken);
}
