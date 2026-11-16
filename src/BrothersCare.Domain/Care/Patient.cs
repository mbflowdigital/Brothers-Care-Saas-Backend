namespace BrothersCare.Domain.Care;

public sealed class Patient
{
    private Patient()
    {
    }

    public Patient(Guid id, Guid tenantId, string fullName, DateOnly birthDate, DateTimeOffset createdAt, string? healthPlan = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Patient and tenant ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        Rename(fullName);
        BirthDate = birthDate;
        ChangeHealthPlan(healthPlan);
        CreatedAt = createdAt;
        Status = PatientStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    public string? HealthPlan { get; private set; }

    public PatientStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
    }

    public void ChangeHealthPlan(string? healthPlan)
    {
        if (string.IsNullOrWhiteSpace(healthPlan))
        {
            HealthPlan = null;
            return;
        }

        var normalized = healthPlan.Trim();
        if (normalized.Length > 150)
        {
            throw new ArgumentException("Health plan cannot exceed 150 characters.", nameof(healthPlan));
        }

        HealthPlan = normalized;
    }

    public void ChangeBirthDate(DateOnly birthDate)
    {
        if (birthDate == default)
        {
            throw new ArgumentException("Birth date is required.", nameof(birthDate));
        }

        BirthDate = birthDate;
    }

    public void Inactivate() => Status = PatientStatus.Inactive;

    public void Activate() => Status = PatientStatus.Active;
}

public enum PatientStatus
{
    Active = 1,
    Inactive = 2,
}
