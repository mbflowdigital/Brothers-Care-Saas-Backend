namespace BrothersCare.Domain.Care;

public sealed class Patient
{
    private Patient()
    {
    }

    public Patient(Guid id, Guid tenantId, string fullName, DateOnly birthDate, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Patient and tenant ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        Rename(fullName);
        BirthDate = birthDate;
        CreatedAt = createdAt;
        Status = PatientStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    public PatientStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
    }

    public void Inactivate() => Status = PatientStatus.Inactive;

    public void Activate() => Status = PatientStatus.Active;
}

public enum PatientStatus
{
    Active = 1,
    Inactive = 2,
}
