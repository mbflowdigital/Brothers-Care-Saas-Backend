using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class CompleteAppointment(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<CompleteAppointmentResult> ExecuteAsync(
        CompleteAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.AppointmentId == Guid.Empty)
        {
            return new(CompleteAppointmentStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => CompleteAppointmentStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => CompleteAppointmentStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => CompleteAppointmentStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var appointment = await transaction.FindAppointmentForUpdateAsync(
            scope.TenantId, scope.Professional.Id, command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return new(CompleteAppointmentStatus.NotFound);
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return new(CompleteAppointmentStatus.NotScheduled);
        }

        var now = timeProvider.GetUtcNow();
        if (now < appointment.StartsAt)
        {
            return new(CompleteAppointmentStatus.NotStarted);
        }

        appointment.MarkCompleted(scope.MembershipId, now);
        await transaction.CommitAsync(cancellationToken);
        return new(CompleteAppointmentStatus.Success, AppointmentResult.From(appointment));
    }
}

public sealed record CompleteAppointmentCommand(Guid AppointmentId);

public enum CompleteAppointmentStatus
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

public sealed record CompleteAppointmentResult(
    CompleteAppointmentStatus Status,
    AppointmentItem? Appointment = null);
