namespace BrothersCare.Domain.Care;

public sealed class Appointment
{
    private Appointment()
    {
    }

    public Appointment(
        Guid id,
        Guid tenantId,
        Guid clinicUnitId,
        Guid patientId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || clinicUnitId == Guid.Empty ||
            patientId == Guid.Empty || professionalId == Guid.Empty)
        {
            throw new ArgumentException("Appointment ids are required.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endsAt, startsAt);

        Id = id;
        TenantId = tenantId;
        ClinicUnitId = clinicUnitId;
        PatientId = patientId;
        ProfessionalId = professionalId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        CreatedAt = createdAt;
        Status = AppointmentStatus.Scheduled;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ClinicUnitId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid ProfessionalId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Reschedule(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endsAt, startsAt);

        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = AppointmentStatus.Scheduled;
    }

    public void MarkCompleted() => Status = AppointmentStatus.Completed;

    public void MarkNoShow() => Status = AppointmentStatus.NoShow;

    public void Cancel() => Status = AppointmentStatus.Cancelled;
}

public enum AppointmentStatus
{
    Scheduled = 1,
    Completed = 2,
    NoShow = 3,
    Cancelled = 4,
}
