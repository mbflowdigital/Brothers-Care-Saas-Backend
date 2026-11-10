using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;

namespace BrothersCare.Application.Tenancy;

public sealed class GetTenantWorkspaceSummary(ICurrentPrincipalAccessor principalAccessor, ITenantWorkspaceStore store)
{
    public async Task<TenantWorkspaceSummary?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId || principal.TenantMembershipId is null) return null;
        var counts = await store.CountAsync(tenantId, cancellationToken);
        return new(counts.TeamMembers, counts.Patients, counts.Appointments);
    }
}

public sealed record TenantWorkspaceSummary(int TeamMembers, int Patients, int Appointments);
