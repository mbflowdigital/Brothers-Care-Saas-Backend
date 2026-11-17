namespace BrothersCare.Domain.Care;

public sealed class ProfessionalAvailabilityWindow
{
    private ProfessionalAvailabilityWindow()
    {
    }

    public ProfessionalAvailabilityWindow(
        Guid id,
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
        DayOfWeek dayOfWeek,
        TimeOnly startsAt,
        TimeOnly endsAt,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || professionalId == Guid.Empty ||
            clinicUnitId == Guid.Empty)
        {
            throw new ArgumentException("Availability ids are required.");
        }

        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        }

        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "Availability end must be after its start.");
        }

        Id = id;
        TenantId = tenantId;
        ProfessionalId = professionalId;
        ClinicUnitId = clinicUnitId;
        DayOfWeek = dayOfWeek;
        StartsAt = startsAt;
        EndsAt = endsAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ClinicUnitId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartsAt { get; private set; }
    public TimeOnly EndsAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class ProfessionalAvailabilityBlock
{
    public const int ReasonMaxLength = 500;

    private ProfessionalAvailabilityBlock()
    {
    }

    public ProfessionalAvailabilityBlock(
        Guid id,
        Guid tenantId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        AvailabilityBlockKind kind,
        string? reason,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || professionalId == Guid.Empty)
        {
            throw new ArgumentException("Availability block ids are required.");
        }

        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), "Availability block end must be after its start.");
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > ReasonMaxLength)
        {
            throw new ArgumentException($"Availability block reason cannot exceed {ReasonMaxLength} characters.", nameof(reason));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Id = id;
        TenantId = tenantId;
        ProfessionalId = professionalId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Kind = kind;
        Reason = normalizedReason;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public AvailabilityBlockKind Kind { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public enum AvailabilityBlockKind
{
    Blocked = 1,
    Vacation = 2,
}
