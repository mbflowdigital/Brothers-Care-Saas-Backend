namespace BrothersCare.Domain.Care;

public sealed class PatientCareAssignment
{
    private PatientCareAssignment()
    {
    }

    public PatientCareAssignment(
        Guid id,
        Guid tenantId,
        Guid patientId,
        Guid professionalId,
        Guid clinicUnitId,
        DateOnly startsOn)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || patientId == Guid.Empty ||
            professionalId == Guid.Empty || clinicUnitId == Guid.Empty)
        {
            throw new ArgumentException("Care assignment ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        PatientId = patientId;
        ProfessionalId = professionalId;
        ClinicUnitId = clinicUnitId;
        StartsOn = startsOn;
        Status = CareAssignmentStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid ProfessionalId { get; private set; }

    public Guid ClinicUnitId { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    public CareAssignmentStatus Status { get; private set; }

    public void End(DateOnly endsOn)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(endsOn, StartsOn);

        EndsOn = endsOn;
        Status = CareAssignmentStatus.Ended;
    }
}

public enum CareAssignmentStatus
{
    Active = 1,
    Ended = 2,
}
