using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Care.Contracts;

public interface IPatientRegistrationStore
{
    Task<IPatientRegistrationTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IPatientRegistrationTransaction : IAsyncDisposable
{
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasActiveClinicAdministratorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<PatientAssignmentTarget?> FindAssignmentTargetAsync(
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PatientProfessionalOption>> ListProfessionalOptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PatientClinicUnitOption>> ListClinicUnitOptionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<PatientPage> ListAsync(
        Guid tenantId,
        Guid? professionalId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<Guid?> FindActiveProfessionalIdAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<Patient?> FindPatientAsync(Guid tenantId, Guid patientId, CancellationToken cancellationToken);

    Task<PatientCareAssignment?> FindActiveAssignmentAsync(
        Guid tenantId,
        Guid patientId,
        CancellationToken cancellationToken);

    void Add(Patient patient);

    void Add(PatientCareAssignment assignment);

    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record PatientAssignmentTarget(Guid ProfessionalId, Guid ClinicUnitId);

public sealed record PatientProfessionalOption(
    Guid Id,
    string Name,
    string Specialty,
    IReadOnlyList<Guid> ClinicUnitIds);

public sealed record PatientClinicUnitOption(Guid Id, string Name);

public sealed record PatientListItem(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    string? HealthPlan,
    string Status,
    Guid? ProfessionalId,
    string? ProfessionalName,
    Guid? ClinicUnitId,
    DateTimeOffset CreatedAt);

public sealed record PatientPage(
    IReadOnlyList<PatientListItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
