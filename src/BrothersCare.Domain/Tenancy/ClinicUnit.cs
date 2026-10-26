namespace BrothersCare.Domain.Tenancy;

public sealed class ClinicUnit
{
    private ClinicUnit()
    {
    }

    public ClinicUnit(Guid id, Guid tenantId, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Unit and tenant ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        Rename(name);
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
