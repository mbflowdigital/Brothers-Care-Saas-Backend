namespace BrothersCare.Domain.Care;

public sealed class ClinicalRecord
{
    private ClinicalRecord()
    {
    }

    public ClinicalRecord(
        Guid id,
        Guid tenantId,
        Guid appointmentId,
        Guid patientId,
        Guid authorMembershipId,
        string content,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || appointmentId == Guid.Empty ||
            patientId == Guid.Empty || authorMembershipId == Guid.Empty)
        {
            throw new ArgumentException("Clinical record ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        AppointmentId = appointmentId;
        PatientId = patientId;
        AuthorMembershipId = authorMembershipId;
        UpdateContent(content);
        CreatedAt = createdAt;
        Status = ClinicalRecordStatus.Draft;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid AuthorMembershipId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public ClinicalRecordStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    public void UpdateContent(string content)
    {
        if (Status == ClinicalRecordStatus.Finalized)
        {
            throw new InvalidOperationException("A finalized clinical record cannot be overwritten.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        Content = content.Trim();
    }

    public void Finalize(DateTimeOffset finalizedAt)
    {
        if (Status == ClinicalRecordStatus.Finalized)
        {
            throw new InvalidOperationException("The clinical record is already finalized.");
        }

        Status = ClinicalRecordStatus.Finalized;
        FinalizedAt = finalizedAt;
    }
}

public enum ClinicalRecordStatus
{
    Draft = 1,
    Finalized = 2,
}
