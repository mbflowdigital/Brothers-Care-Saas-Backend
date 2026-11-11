namespace BrothersCare.Application.Tenancy.Contracts;

public interface ITenantWorkspaceStore
{
    Task<TenantWorkspaceCounts> CountAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record TenantWorkspaceCounts(int TeamMembers, int Patients, int Appointments);
