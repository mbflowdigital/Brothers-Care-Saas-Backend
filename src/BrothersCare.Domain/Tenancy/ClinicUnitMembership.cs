namespace BrothersCare.Domain.Tenancy;

public sealed class ClinicUnitMembership
{
    private ClinicUnitMembership()
    {
    }

    public ClinicUnitMembership(Guid id, Guid tenantId, Guid clinicUnitId, Guid tenantMembershipId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || clinicUnitId == Guid.Empty || tenantMembershipId == Guid.Empty)
        {
            throw new ArgumentException("Unit membership ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        ClinicUnitId = clinicUnitId;
        TenantMembershipId = tenantMembershipId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ClinicUnitId { get; private set; }

    public Guid TenantMembershipId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
