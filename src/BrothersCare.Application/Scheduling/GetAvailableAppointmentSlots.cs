using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class GetAvailableAppointmentSlots(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    private static readonly TimeOnly FirstSlotTime = new(7, 0);
    private const int SlotIntervalMinutes = 30;
    private const int SlotsPerDay = 28;

    public async Task<GetAvailableAppointmentSlotsResult> ExecuteAsync(
        Guid clinicUnitId,
        DateOnly date,
        int durationMinutes,
        Guid? patientId,
        Guid? appointmentId,
        CancellationToken cancellationToken)
    {
        if (clinicUnitId == Guid.Empty ||
            durationMinutes <= 0 ||
            durationMinutes > Appointment.MaximumDuration.TotalMinutes ||
            (patientId is null) == (appointmentId is null) ||
            patientId == Guid.Empty ||
            appointmentId == Guid.Empty)
        {
            return new(AvailableAppointmentSlotsStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => AvailableAppointmentSlotsStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => AvailableAppointmentSlotsStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => AvailableAppointmentSlotsStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        if (!await transaction.IsActiveUnitLinkedAsync(
                scope.TenantId, scope.MembershipId, clinicUnitId, cancellationToken))
        {
            return new(AvailableAppointmentSlotsStatus.ClinicUnitNotAvailable);
        }

        Guid resolvedPatientId;
        Guid? excludedAppointmentId = null;
        if (appointmentId is not null)
        {
            var appointment = await transaction.FindAppointmentForUpdateAsync(
                scope.TenantId, scope.Professional.Id, appointmentId.Value, cancellationToken);
            if (appointment is null)
            {
                return new(AvailableAppointmentSlotsStatus.AppointmentNotFound);
            }

            if (appointment.Status != AppointmentStatus.Scheduled)
            {
                return new(AvailableAppointmentSlotsStatus.AppointmentNotScheduled);
            }

            resolvedPatientId = appointment.PatientId;
            excludedAppointmentId = appointment.Id;
        }
        else
        {
            resolvedPatientId = patientId!.Value;
            if (!await transaction.IsActivePatientAssignedAsync(
                    scope.TenantId, scope.Professional.Id, resolvedPatientId, cancellationToken))
            {
                return new(AvailableAppointmentSlotsStatus.PatientNotAssigned);
            }
        }

        var units = await transaction.ListClinicUnitOptionsAsync(
            scope.TenantId, scope.MembershipId, cancellationToken);
        var unit = units.SingleOrDefault(item => item.Id == clinicUnitId)
            ?? throw new InvalidOperationException("An active linked unit must have an availability option.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(unit.TimeZoneId);
        var windows = await transaction.ListAvailabilityWindowsAsync(
            scope.TenantId, scope.Professional.Id, clinicUnitId, cancellationToken);
        var scheduleConfigured = windows.Count > 0;
        var dayWindows = windows.Where(item => item.DayOfWeek == date.DayOfWeek).ToArray();
        var now = timeProvider.GetUtcNow();
        var candidates = new List<AvailableAppointmentSlot>(SlotsPerDay);

        for (var index = 0; index < SlotsPerDay; index++)
        {
            var startTime = FirstSlotTime.AddMinutes(index * SlotIntervalMinutes);
            var localStart = date.ToDateTime(startTime, DateTimeKind.Unspecified);
            var localEnd = localStart.AddMinutes(durationMinutes);

            if (scheduleConfigured &&
                (localEnd.Date != localStart.Date ||
                 !dayWindows.Any(window =>
                     window.StartsAt <= TimeOnly.FromDateTime(localStart) &&
                     window.EndsAt >= TimeOnly.FromDateTime(localEnd))))
            {
                continue;
            }

            if (!TryCreateOffset(localStart, timeZone, out var startsAt) ||
                !TryCreateOffset(localEnd, timeZone, out var endsAt) ||
                startsAt < now)
            {
                continue;
            }

            candidates.Add(new(startsAt, endsAt));
        }

        if (candidates.Count == 0)
        {
            return new(AvailableAppointmentSlotsStatus.Success, []);
        }

        var firstStart = candidates.Min(item => item.StartsAt);
        var lastEnd = candidates.Max(item => item.EndsAt);
        var blocks = await transaction.ListAvailabilityBlocksAsync(
            scope.TenantId, scope.Professional.Id, firstStart, lastEnd, cancellationToken);
        var conflicts = await transaction.ListSchedulingConflictWindowsAsync(
            scope.TenantId,
            scope.Professional.Id,
            resolvedPatientId,
            firstStart - Appointment.MaximumDuration,
            lastEnd,
            excludedAppointmentId,
            cancellationToken);

        var available = candidates.Where(slot =>
                !blocks.Any(block => block.StartsAt < slot.EndsAt && block.EndsAt > slot.StartsAt) &&
                !conflicts.Any(conflict =>
                    conflict.StartsAt < slot.EndsAt && conflict.EndsAt > slot.StartsAt &&
                    (conflict.ProfessionalId == scope.Professional.Id ||
                     conflict.PatientId == resolvedPatientId)))
            .ToArray();

        return new(AvailableAppointmentSlotsStatus.Success, available);
    }

    private static bool TryCreateOffset(
        DateTime localDateTime,
        TimeZoneInfo timeZone,
        out DateTimeOffset value)
    {
        if (timeZone.IsInvalidTime(localDateTime) || timeZone.IsAmbiguousTime(localDateTime))
        {
            value = default;
            return false;
        }

        value = new DateTimeOffset(localDateTime, timeZone.GetUtcOffset(localDateTime));
        return true;
    }
}

public enum AvailableAppointmentSlotsStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    ProfessionalNotFound = 5,
    PatientNotAssigned = 6,
    ClinicUnitNotAvailable = 7,
    AppointmentNotFound = 8,
    AppointmentNotScheduled = 9,
}

public sealed record GetAvailableAppointmentSlotsResult(
    AvailableAppointmentSlotsStatus Status,
    IReadOnlyList<AvailableAppointmentSlot>? Slots = null);

public sealed record AvailableAppointmentSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
