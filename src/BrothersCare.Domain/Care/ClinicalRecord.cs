namespace BrothersCare.Domain.Care;

public sealed class ClinicalRecord
{
    public const int MaximumSectionLength = 100_000;

    private ClinicalRecord()
    {
    }

    public ClinicalRecord(
        Guid id,
        Guid tenantId,
        Guid patientId,
        Guid clinicUnitId,
        Guid authorMembershipId,
        Guid? appointmentId,
        string? patientReport,
        string? proceduresAndObservations,
        string? assessmentAndPlan,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || patientId == Guid.Empty ||
            clinicUnitId == Guid.Empty || authorMembershipId == Guid.Empty || appointmentId == Guid.Empty)
        {
            throw new ArgumentException("Clinical record ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        PatientId = patientId;
        ClinicUnitId = clinicUnitId;
        AuthorMembershipId = authorMembershipId;
        AppointmentId = appointmentId;
        CreatedAt = createdAt;
        Status = ClinicalRecordStatus.Draft;
        Update(patientReport, proceduresAndObservations, assessmentAndPlan, createdAt);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid ClinicUnitId { get; private set; }

    public Guid AuthorMembershipId { get; private set; }

    public string PatientReport { get; private set; } = string.Empty;

    public string ProceduresAndObservations { get; private set; } = string.Empty;

    public string AssessmentAndPlan { get; private set; } = string.Empty;

    public ClinicalRecordStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    public byte[] Version { get; private set; } = [];

    public void Update(
        string? patientReport,
        string? proceduresAndObservations,
        string? assessmentAndPlan,
        DateTimeOffset updatedAt)
    {
        if (Status == ClinicalRecordStatus.Finalized)
        {
            throw new InvalidOperationException("A finalized clinical record cannot be overwritten.");
        }

        PatientReport = NormalizeSection(patientReport);
        ProceduresAndObservations = NormalizeSection(proceduresAndObservations);
        AssessmentAndPlan = NormalizeSection(assessmentAndPlan);
        UpdatedAt = updatedAt;
    }

    public void Finalize(DateTimeOffset finalizedAt)
    {
        if (Status == ClinicalRecordStatus.Finalized)
        {
            throw new InvalidOperationException("The clinical record is already finalized.");
        }

        if (string.IsNullOrWhiteSpace(ProceduresAndObservations) ||
            string.IsNullOrWhiteSpace(AssessmentAndPlan))
        {
            throw new InvalidOperationException("Procedures and assessment are required to finalize a clinical record.");
        }

        Status = ClinicalRecordStatus.Finalized;
        FinalizedAt = finalizedAt;
        UpdatedAt = finalizedAt;
    }

    private static string NormalizeSection(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > MaximumSectionLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Clinical sections cannot exceed {MaximumSectionLength} characters.");
        }

        return normalized;
    }
}

public enum ClinicalRecordStatus
{
    Draft = 1,
    Finalized = 2,
}
