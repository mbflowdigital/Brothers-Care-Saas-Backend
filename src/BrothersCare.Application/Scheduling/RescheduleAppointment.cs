using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class RescheduleAppointment(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<RescheduleAppointmentResult> ExecuteAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsValid(command))
        {
            return new(RescheduleAppointmentStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => RescheduleAppointmentStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => RescheduleAppointmentStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => RescheduleAppointmentStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var now = timeProvider.GetUtcNow();
        var appointment = await transaction.FindAppointmentForUpdateAsync(
            scope.TenantId, scope.Professional.Id, command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return new(RescheduleAppointmentStatus.NotFound);
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return new(RescheduleAppointmentStatus.NotScheduled);
        }

        if (!await transaction.IsActiveUnitLinkedAsync(
                scope.TenantId, scope.MembershipId, command.ClinicUnitId, cancellationToken))
        {
            return new(RescheduleAppointmentStatus.ClinicUnitNotAvailable);
        }

        if (command.StartsAt < now)
        {
            return new(RescheduleAppointmentStatus.StartsInPast);
        }

        var conflicts = await transaction.FindConflictsAsync(
            scope.TenantId,
            scope.Professional.Id,
            appointment.PatientId,
            command.StartsAt,
            command.EndsAt,
            appointment.Id,
            cancellationToken);
        if (conflicts.Professional)
        {
            return new(RescheduleAppointmentStatus.ProfessionalConflict);
        }

        if (conflicts.Patient)
        {
            return new(RescheduleAppointmentStatus.PatientConflict);
        }

        appointment.Reschedule(
            command.ClinicUnitId,
            command.StartsAt,
            command.EndsAt,
            scope.MembershipId,
            now);
        await transaction.CommitAsync(cancellationToken);

        return new(RescheduleAppointmentStatus.Success, new AppointmentItem(
            appointment.Id,
            appointment.ClinicUnitId,
            appointment.PatientId,
            appointment.ProfessionalId,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.Notes,
            appointment.Status.ToString(),
            appointment.CreatedAt));
    }

    private static bool IsValid(RescheduleAppointmentCommand command) =>
        command.AppointmentId != Guid.Empty &&
        command.ClinicUnitId != Guid.Empty &&
        command.EndsAt > command.StartsAt &&
        command.EndsAt - command.StartsAt <= Appointment.MaximumDuration;
}

public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    Guid ClinicUnitId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public enum RescheduleAppointmentStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    ProfessionalNotFound = 5,
    NotFound = 6,
    NotScheduled = 7,
    ClinicUnitNotAvailable = 8,
    StartsInPast = 9,
    ProfessionalConflict = 10,
    PatientConflict = 11,
}

public sealed record RescheduleAppointmentResult(
    RescheduleAppointmentStatus Status,
    AppointmentItem? Appointment = null);
