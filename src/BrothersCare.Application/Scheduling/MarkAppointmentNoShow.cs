using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class MarkAppointmentNoShow(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<MarkAppointmentNoShowResult> ExecuteAsync(
        MarkAppointmentNoShowCommand command,
        CancellationToken cancellationToken)
    {
        if (command.AppointmentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Reason) ||
            command.Reason.Trim().Length > Appointment.ReasonMaxLength)
        {
            return new(MarkAppointmentNoShowStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => MarkAppointmentNoShowStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => MarkAppointmentNoShowStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => MarkAppointmentNoShowStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var appointment = await transaction.FindAppointmentForUpdateAsync(
            scope.TenantId, scope.Professional.Id, command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return new(MarkAppointmentNoShowStatus.NotFound);
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return new(MarkAppointmentNoShowStatus.NotScheduled);
        }

        var now = timeProvider.GetUtcNow();
        if (now < appointment.StartsAt)
        {
            return new(MarkAppointmentNoShowStatus.NotStarted);
        }

        appointment.MarkNoShow(command.Reason, scope.MembershipId, now);
        await transaction.CommitAsync(cancellationToken);
        return new(MarkAppointmentNoShowStatus.Success, AppointmentResult.From(appointment));
    }
}

public sealed record MarkAppointmentNoShowCommand(Guid AppointmentId, string? Reason);

public enum MarkAppointmentNoShowStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    ProfessionalNotFound = 5,
    NotFound = 6,
    NotScheduled = 7,
    NotStarted = 8,
}

public sealed record MarkAppointmentNoShowResult(
    MarkAppointmentNoShowStatus Status,
    AppointmentItem? Appointment = null);

internal static class AppointmentResult
{
    public static AppointmentItem From(Appointment appointment) => new(
        appointment.Id,
        appointment.ClinicUnitId,
        appointment.PatientId,
        appointment.ProfessionalId,
        appointment.StartsAt,
        appointment.EndsAt,
        appointment.Notes,
        appointment.Status.ToString(),
        appointment.CreatedAt);
}
