using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Care;

public sealed class ManagePatient(
    ICurrentPrincipalAccessor principalAccessor,
    IPatientRegistrationStore store,
    TimeProvider timeProvider)
{
    public Task<PatientRegistrationResult> UpdateAsync(
        Guid patientId,
        string fullName,
        DateOnly birthDate,
        string? healthPlan,
        Guid? professionalId,
        Guid? clinicUnitId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(patientId, fullName, birthDate, healthPlan, professionalId, clinicUnitId, false, cancellationToken);

    public Task<PatientRegistrationResult> DeactivateAsync(Guid patientId, CancellationToken cancellationToken) =>
        ExecuteAsync(patientId, null, default, null, null, null, true, cancellationToken);

    private async Task<PatientRegistrationResult> ExecuteAsync(
        Guid patientId,
        string? fullName,
        DateOnly birthDate,
        string? healthPlan,
        Guid? professionalId,
        Guid? clinicUnitId,
        bool deactivate,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty || (!deactivate &&
            (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 200 || birthDate == default ||
             NormalizeOptional(healthPlan)?.Length > 150 || professionalId.HasValue != clinicUnitId.HasValue ||
             professionalId == Guid.Empty || clinicUnitId == Guid.Empty)))
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

        var patient = await transaction.FindPatientAsync(tenantId, patientId, cancellationToken);
        if (patient is null)
        {
            return new(PatientRegistrationStatus.NotFound);
        }

        var activeAssignment = await transaction.FindActiveAssignmentAsync(tenantId, patientId, cancellationToken);
        if (deactivate)
        {
            patient.Inactivate();
            activeAssignment?.End(DateOnly.FromDateTime(now.UtcDateTime));
        }
        else
        {
            PatientAssignmentTarget? target = null;
            if (professionalId is { } selectedProfessionalId && clinicUnitId is { } selectedClinicUnitId)
            {
                target = await transaction.FindAssignmentTargetAsync(
                    tenantId, selectedProfessionalId, selectedClinicUnitId, cancellationToken);
                if (target is null) return new(PatientRegistrationStatus.AssignmentTargetNotFound);
            }

            patient.Rename(fullName!);
            patient.ChangeBirthDate(birthDate);
            patient.ChangeHealthPlan(healthPlan);
            var assignmentChanged = activeAssignment?.ProfessionalId != target?.ProfessionalId ||
                activeAssignment?.ClinicUnitId != target?.ClinicUnitId;
            if (assignmentChanged)
            {
                activeAssignment?.End(DateOnly.FromDateTime(now.UtcDateTime));
                if (target is not null)
                {
                    transaction.Add(new PatientCareAssignment(Guid.NewGuid(), tenantId, patient.Id,
                        target.ProfessionalId, target.ClinicUnitId, DateOnly.FromDateTime(now.UtcDateTime)));
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var currentAssignment = deactivate ? null : await transaction.FindActiveAssignmentAsync(tenantId, patientId, cancellationToken);
        return new(PatientRegistrationStatus.Success,
            new(patient.Id, patient.FullName, patient.BirthDate, patient.HealthPlan, patient.Status.ToString(),
                currentAssignment?.ProfessionalId, currentAssignment?.ClinicUnitId, patient.CreatedAt));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
