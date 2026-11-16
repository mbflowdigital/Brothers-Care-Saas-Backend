namespace BrothersCare.Domain.Care;

public sealed class Appointment
{
    public const int NotesMaxLength = 1000;
    public const int ReasonMaxLength = 500;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(24);

    private Appointment()
    {
    }

    public Appointment(
        Guid id,
        Guid tenantId,
        Guid clinicUnitId,
        Guid patientId,
        Guid professionalId,
        Guid createdByMembershipId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string? notes,
        DateTimeOffset createdAt)
    {
        ValidateRequiredIds(id, tenantId, clinicUnitId, patientId, professionalId, createdByMembershipId);
        ValidatePeriod(startsAt, endsAt);

        Id = id;
        TenantId = tenantId;
        ClinicUnitId = clinicUnitId;
        PatientId = patientId;
        ProfessionalId = professionalId;
        CreatedByMembershipId = createdByMembershipId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Notes = NormalizeOptional(notes, NotesMaxLength, nameof(notes));
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = AppointmentStatus.Scheduled;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClinicUnitId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid? CreatedByMembershipId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Notes { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedByMembershipId { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByMembershipId { get; private set; }
    public DateTimeOffset? NoShowAt { get; private set; }
    public Guid? NoShowByMembershipId { get; private set; }
    public string? NoShowReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledByMembershipId { get; private set; }
    public string? CancellationReason { get; private set; }

    public void Reschedule(Guid clinicUnitId, DateTimeOffset startsAt, DateTimeOffset endsAt,
        Guid updatedByMembershipId, DateTimeOffset updatedAt)
    {
        EnsureScheduled();
        ValidateRequiredId(clinicUnitId, nameof(clinicUnitId));
        ValidateRequiredId(updatedByMembershipId, nameof(updatedByMembershipId));
        ValidatePeriod(startsAt, endsAt);

        ClinicUnitId = clinicUnitId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        RegisterUpdate(updatedByMembershipId, updatedAt);
    }

    public void MarkCompleted(Guid completedByMembershipId, DateTimeOffset completedAt)
    {
        EnsureScheduled();
        ValidateRequiredId(completedByMembershipId, nameof(completedByMembershipId));
        EnsureAppointmentStarted(completedAt);

        Status = AppointmentStatus.Completed;
        CompletedAt = completedAt;
        CompletedByMembershipId = completedByMembershipId;
        RegisterUpdate(completedByMembershipId, completedAt);
    }

    public void MarkNoShow(string reason, Guid markedByMembershipId, DateTimeOffset markedAt)
    {
        EnsureScheduled();
        ValidateRequiredId(markedByMembershipId, nameof(markedByMembershipId));
        EnsureAppointmentStarted(markedAt);
        var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason));

        Status = AppointmentStatus.NoShow;
        NoShowAt = markedAt;
        NoShowByMembershipId = markedByMembershipId;
        NoShowReason = normalizedReason;
        RegisterUpdate(markedByMembershipId, markedAt);
    }

    public void Cancel(string reason, Guid cancelledByMembershipId, DateTimeOffset cancelledAt)
    {
        EnsureScheduled();
        ValidateRequiredId(cancelledByMembershipId, nameof(cancelledByMembershipId));
        var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason));

        Status = AppointmentStatus.Cancelled;
        CancelledAt = cancelledAt;
        CancelledByMembershipId = cancelledByMembershipId;
        CancellationReason = normalizedReason;
        RegisterUpdate(cancelledByMembershipId, cancelledAt);
    }

    private static void ValidateRequiredIds(params Guid[] ids)
    {
        if (ids.Any(id => id == Guid.Empty)) throw new ArgumentException("Appointment ids are required.");
    }

    private static void ValidateRequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("The id is required.", parameterName);
    }

    private static void ValidatePeriod(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endsAt, startsAt);
        if (endsAt - startsAt > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(endsAt), "Appointment duration cannot exceed 24 hours.");
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return Normalize(value, maxLength, parameterName);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maxLength, parameterName);

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return normalized;
    }

    private void EnsureScheduled()
    {
        if (Status != AppointmentStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled appointments can transition or be rescheduled.");
    }

    private void EnsureAppointmentStarted(DateTimeOffset occurredAt)
    {
        if (occurredAt < StartsAt)
            throw new InvalidOperationException("The appointment cannot be completed or marked as no-show before it starts.");
    }

    private void RegisterUpdate(Guid membershipId, DateTimeOffset updatedAt)
    {
        UpdatedByMembershipId = membershipId;
        UpdatedAt = updatedAt;
    }
}

public enum AppointmentStatus
{
    Scheduled = 1,
    Completed = 2,
    NoShow = 3,
    Cancelled = 4,
}
