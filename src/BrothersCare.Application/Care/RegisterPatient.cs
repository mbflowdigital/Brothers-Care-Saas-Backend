using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Care;

public sealed class RegisterPatient(
    ICurrentPrincipalAccessor principalAccessor,
    IPatientRegistrationStore store,
    TimeProvider timeProvider)
{
    public async Task<PatientRegistrationResult> ExecuteAsync(
        string fullName,
        DateOnly birthDate,
        string? healthPlan,
        Guid? professionalId,
        Guid? clinicUnitId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 200 || birthDate == default ||
            NormalizeOptional(healthPlan)?.Length > 150 ||
            professionalId.HasValue != clinicUnitId.HasValue ||
            professionalId == Guid.Empty || clinicUnitId == Guid.Empty)
        {
            return new(PatientRegistrationStatus.InvalidInput);
        }

        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId)
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(now) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Unauthenticated);
        }

        if (session.ActiveTenantId != tenantId ||
            !await transaction.HasActiveClinicAdministratorAsync(tenantId, principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        PatientAssignmentTarget? assignmentTarget = null;
        if (professionalId is { } selectedProfessionalId && clinicUnitId is { } selectedClinicUnitId)
        {
            assignmentTarget = await transaction.FindAssignmentTargetAsync(
                tenantId, selectedProfessionalId, selectedClinicUnitId, cancellationToken);
            if (assignmentTarget is null)
            {
                return new(PatientRegistrationStatus.AssignmentTargetNotFound);
            }
        }

        var patient = new Patient(Guid.NewGuid(), tenantId, fullName, birthDate, now, healthPlan);
        transaction.Add(patient);
        if (assignmentTarget is not null)
        {
            transaction.Add(new PatientCareAssignment(
                Guid.NewGuid(),
                tenantId,
                patient.Id,
                assignmentTarget.ProfessionalId,
                assignmentTarget.ClinicUnitId,
                DateOnly.FromDateTime(now.UtcDateTime)));
        }
        await transaction.CommitAsync(cancellationToken);

        return new(PatientRegistrationStatus.Success,
            new(patient.Id, patient.FullName, patient.BirthDate, patient.HealthPlan, patient.Status.ToString(),
                assignmentTarget?.ProfessionalId, assignmentTarget?.ClinicUnitId, patient.CreatedAt));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum PatientRegistrationStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    AssignmentTargetNotFound = 5,
    NotFound = 6,
}

public sealed record PatientRegistrationItem(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    string? HealthPlan,
    string Status,
    Guid? ProfessionalId,
    Guid? ClinicUnitId,
    DateTimeOffset CreatedAt);

public sealed record PatientRegistrationResult(
    PatientRegistrationStatus Status,
    PatientRegistrationItem? Patient = null);
