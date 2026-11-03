namespace BrothersCare.Domain.Tenancy;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(Guid id, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(id));
        }

        Id = id;
        Rename(name);
        CreatedAt = createdAt;
        Status = TenantStatus.Active;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Suspend()
    {
        if (Status is not TenantStatus.Active)
        {
            throw new InvalidOperationException("Only an active tenant can be suspended.");
        }

        Status = TenantStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status is not TenantStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended tenant can be reactivated.");
        }

        Status = TenantStatus.Active;
    }

    public void Deactivate()
    {
        if (Status is TenantStatus.Deactivated)
        {
            throw new InvalidOperationException("The tenant is already deactivated.");
        }

        Status = TenantStatus.Deactivated;
    }
}

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
}
