using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class CancelAppointment(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<CancelAppointmentResult> ExecuteAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.AppointmentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Reason) ||
            command.Reason.Trim().Length > Appointment.ReasonMaxLength)
        {
            return new(CancelAppointmentStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAppointmentManagerAsync(
            principalAccessor.Principal, command.ProfessionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => CancelAppointmentStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => CancelAppointmentStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => CancelAppointmentStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var appointment = await transaction.FindAppointmentForUpdateAsync(
            scope.TenantId, scope.Professional.Id, command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return new(CancelAppointmentStatus.NotFound);
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return new(CancelAppointmentStatus.NotScheduled);
        }

        if (!await transaction.IsActiveUnitLinkedAsync(
                scope.TenantId, scope.MembershipId, appointment.ClinicUnitId, cancellationToken))
        {
            return new(CancelAppointmentStatus.Forbidden);
        }

        var now = timeProvider.GetUtcNow();
        appointment.Cancel(command.Reason, scope.MembershipId, now);
        await transaction.CommitAsync(cancellationToken);

        return new(CancelAppointmentStatus.Success, new AppointmentItem(
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
}

public sealed record CancelAppointmentCommand(Guid AppointmentId, Guid? ProfessionalId, string? Reason)
{
    public CancelAppointmentCommand(Guid appointmentId, string? reason)
        : this(appointmentId, null, reason)
    {
    }
}

public enum CancelAppointmentStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    ProfessionalNotFound = 5,
    NotFound = 6,
    NotScheduled = 7,
}

public sealed record CancelAppointmentResult(
    CancelAppointmentStatus Status,
    AppointmentItem? Appointment = null);
