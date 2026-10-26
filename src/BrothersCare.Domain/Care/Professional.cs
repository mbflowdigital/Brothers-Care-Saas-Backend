namespace BrothersCare.Domain.Care;

public sealed class Professional
{
    private Professional()
    {
    }

    public Professional(
        Guid id,
        Guid tenantId,
        string fullName,
        string specialty,
        string registrationNumber,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Professional and tenant ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        UpdateDetails(fullName, specialty, registrationNumber);
        CreatedAt = createdAt;
        Status = ProfessionalStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string Specialty { get; private set; } = string.Empty;

    public string RegistrationNumber { get; private set; } = string.Empty;

    public ProfessionalStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateDetails(string fullName, string specialty, string registrationNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(specialty);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationNumber);
        FullName = fullName.Trim();
        Specialty = specialty.Trim();
        RegistrationNumber = registrationNumber.Trim();
    }

    public void Inactivate() => Status = ProfessionalStatus.Inactive;

    public void Activate() => Status = ProfessionalStatus.Active;
}

public enum ProfessionalStatus
{
    Active = 1,
    Inactive = 2,
}
