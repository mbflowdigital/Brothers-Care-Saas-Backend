using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Care.Contracts;

public interface IClinicalRecordStore
{
    Task<IClinicalRecordTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IClinicalRecordTransaction : IAsyncDisposable
{
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<ClinicalAuthorContext?> FindAuthorContextAsync(
        Guid tenantId, Guid userId, Guid membershipId, CancellationToken cancellationToken);
    Task<bool> HasCareAccessAsync(
        Guid tenantId, Guid professionalId, Guid patientId, Guid clinicUnitId, CancellationToken cancellationToken);
    Task<ClinicalAppointmentContext?> FindAppointmentContextAsync(
        Guid tenantId, Guid appointmentId, CancellationToken cancellationToken);
    Task<bool> HasRecordForAppointmentAsync(
        Guid tenantId, Guid appointmentId, CancellationToken cancellationToken);
    Task<ClinicalRecord?> FindTrackedAsync(Guid tenantId, Guid recordId, CancellationToken cancellationToken);
    Task<ClinicalRecordDetails?> GetAsync(Guid tenantId, Guid recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ClinicalRecordSummary>> ListAsync(
        Guid tenantId, Guid professionalId, Guid patientId, CancellationToken cancellationToken);
    Task<ClinicalRecordOptions> ListOptionsAsync(
        Guid tenantId, Guid professionalId, CancellationToken cancellationToken);
    void Add(ClinicalRecord record);
    Task<bool> SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record ClinicalAuthorContext(Guid MembershipId, Guid ProfessionalId, string Name, string? Specialty);
public sealed record ClinicalAppointmentContext(
    Guid Id,
    Guid PatientId,
    Guid ClinicUnitId,
    Guid ProfessionalId,
    AppointmentStatus Status);
public sealed record ClinicalPatientOption(Guid Id, string Name, IReadOnlyList<Guid> ClinicUnitIds);
public sealed record ClinicalUnitOption(Guid Id, string Name);
public sealed record ClinicalRecordOptions(
    IReadOnlyList<ClinicalPatientOption> Patients,
    IReadOnlyList<ClinicalUnitOption> Units,
    ClinicalAuthorContext Author);

public sealed record ClinicalRecordSummary(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid ClinicUnitId,
    string ClinicUnitName,
    string AuthorName,
    string? AuthorSpecialty,
    string Status,
    string Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinalizedAt);

public sealed record ClinicalRecordDetails(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid ClinicUnitId,
    string ClinicUnitName,
    Guid AuthorMembershipId,
    string AuthorName,
    string? AuthorSpecialty,
    Guid? AppointmentId,
    string PatientReport,
    string ProceduresAndObservations,
    string AssessmentAndPlan,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinalizedAt,
    string Version);
