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
        string? specialty,
        string? registrationNumber,
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

    public Guid? TenantMembershipId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string Specialty { get; private set; } = string.Empty;

    public string? CouncilType { get; private set; }

    public string? CouncilState { get; private set; }

    public string RegistrationNumber { get; private set; } = string.Empty;

    public int? DefaultAppointmentDurationMinutes { get; private set; }

    public ProfessionalStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateDetails(string fullName, string? specialty, string? registrationNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
        Specialty = specialty?.Trim() ?? string.Empty;
        RegistrationNumber = registrationNumber?.Trim() ?? string.Empty;
    }

    public void Inactivate() => Status = ProfessionalStatus.Inactive;

    public void Activate() => Status = ProfessionalStatus.Active;

    public void ConfigureForMembership(
        Guid tenantMembershipId,
        string fullName,
        string? specialty,
        string? councilType,
        string? registrationNumber,
        string? councilState = null,
        int? defaultAppointmentDurationMinutes = null)
    {
        if (tenantMembershipId == Guid.Empty)
        {
            throw new ArgumentException("Tenant membership id is required.", nameof(tenantMembershipId));
        }

        if (defaultAppointmentDurationMinutes is < 5 or > 480)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultAppointmentDurationMinutes));
        }

        TenantMembershipId = tenantMembershipId;
        CouncilType = string.IsNullOrWhiteSpace(councilType) ? null : councilType.Trim();
        CouncilState = string.IsNullOrWhiteSpace(councilState) ? null : councilState.Trim().ToUpperInvariant();
        DefaultAppointmentDurationMinutes = defaultAppointmentDurationMinutes;
        UpdateDetails(fullName, specialty, registrationNumber);
        Activate();
    }
}

public enum ProfessionalStatus
{
    Active = 1,
    Inactive = 2,
}
