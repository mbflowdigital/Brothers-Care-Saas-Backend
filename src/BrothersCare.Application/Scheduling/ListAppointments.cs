using System.Globalization;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class ListAppointments(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan MaximumPeriod = TimeSpan.FromDays(93);

    public async Task<ListAppointmentsResult> ExecuteAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? status,
        Guid? professionalId,
        Guid? clinicUnitId,
        CancellationToken cancellationToken)
    {
        if (from > to || to - from > MaximumPeriod ||
            professionalId == Guid.Empty || clinicUnitId == Guid.Empty ||
            !TryParseStatus(status, out var parsedStatus))
        {
            return new(AppointmentQueryStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAgendaViewerAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => AppointmentQueryStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => AppointmentQueryStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => AppointmentQueryStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful agenda access must include its scope.");
        if (!scope.IsClinicAdministrator &&
            professionalId is { } requestedProfessionalId &&
            requestedProfessionalId != scope.ProfessionalId)
        {
            return new(AppointmentQueryStatus.Forbidden);
        }

        var appointments = await transaction.ListAppointmentsAsync(
            scope.TenantId,
            scope.IsClinicAdministrator ? professionalId : scope.ProfessionalId,
            clinicUnitId,
            from,
            to,
            parsedStatus,
            cancellationToken);
        return new(AppointmentQueryStatus.Success, appointments);
    }

    private static bool TryParseStatus(string? value, out AppointmentStatus? status)
    {
        status = null;
        if (value is null)
        {
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !Enum.TryParse(value, ignoreCase: true, out AppointmentStatus parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        status = parsed;
        return true;
    }
}

public sealed class GetAppointment(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<GetAppointmentResult> ExecuteAsync(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAgendaViewerAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => AppointmentQueryStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => AppointmentQueryStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => AppointmentQueryStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var appointment = await transaction.FindAppointmentAsync(
            scope.TenantId, scope.ProfessionalId, appointmentId, cancellationToken);
        return appointment is null
            ? new(AppointmentQueryStatus.NotFound)
            : new(AppointmentQueryStatus.Success, appointment);
    }
}

public enum AppointmentQueryStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    ProfessionalNotFound = 4,
    InvalidInput = 5,
    NotFound = 6,
}

public sealed record ListAppointmentsResult(
    AppointmentQueryStatus Status,
    IReadOnlyList<AppointmentScheduleItem>? Appointments = null);

public sealed record GetAppointmentResult(
    AppointmentQueryStatus Status,
    AppointmentScheduleItem? Appointment = null);
