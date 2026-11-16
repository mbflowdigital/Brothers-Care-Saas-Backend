using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;

namespace BrothersCare.Application.Scheduling;

public sealed class GetSchedulingOptions(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<SchedulingOptionsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => SchedulingOptionsStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => SchedulingOptionsStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => SchedulingOptionsStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var patients = await transaction.ListPatientOptionsAsync(
            scope.TenantId, scope.Professional.Id, cancellationToken);
        var clinicUnits = await transaction.ListClinicUnitOptionsAsync(
            scope.TenantId, scope.MembershipId, cancellationToken);
        return new(SchedulingOptionsStatus.Success, new(
            patients,
            clinicUnits,
            scope.Professional.DefaultAppointmentDurationMinutes));
    }
}

public enum SchedulingOptionsStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    ProfessionalNotFound = 4,
}

public sealed record SchedulingOptions(
    IReadOnlyList<SchedulingPatientOption> Patients,
    IReadOnlyList<SchedulingClinicUnitOption> ClinicUnits,
    int? DefaultAppointmentDurationMinutes);

public sealed record SchedulingOptionsResult(
    SchedulingOptionsStatus Status,
    SchedulingOptions? Options = null);
