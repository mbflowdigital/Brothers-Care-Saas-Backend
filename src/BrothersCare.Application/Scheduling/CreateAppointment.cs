using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class CreateAppointment(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<CreateAppointmentResult> ExecuteAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedNotes = NormalizeOptional(command.Notes);
        if (!IsValid(command, normalizedNotes))
        {
            return new(CreateAppointmentStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveProfessionalAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => CreateAppointmentStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => CreateAppointmentStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => CreateAppointmentStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful scheduling access must include its scope.");
        var tenantId = scope.TenantId;
        var membershipId = scope.MembershipId;
        var professional = scope.Professional;
        var now = timeProvider.GetUtcNow();

        if (!await transaction.IsActiveUnitLinkedAsync(
                tenantId, membershipId, command.ClinicUnitId, cancellationToken))
        {
            return new(CreateAppointmentStatus.ClinicUnitNotAvailable);
        }

        if (!await transaction.IsActivePatientAssignedAsync(
                tenantId, professional.Id, command.PatientId, cancellationToken))
        {
            return new(CreateAppointmentStatus.PatientNotAssigned);
        }

        if (command.StartsAt < now)
        {
            return new(CreateAppointmentStatus.StartsInPast);
        }

        var conflicts = await transaction.FindConflictsAsync(
            tenantId,
            professional.Id,
            command.PatientId,
            command.StartsAt,
            command.EndsAt,
            excludedAppointmentId: null,
            cancellationToken);
        if (conflicts.Professional)
        {
            return new(CreateAppointmentStatus.ProfessionalConflict);
        }

        if (conflicts.Patient)
        {
            return new(CreateAppointmentStatus.PatientConflict);
        }

        var appointment = new Appointment(
            Guid.NewGuid(),
            tenantId,
            command.ClinicUnitId,
            command.PatientId,
            professional.Id,
            membershipId,
            command.StartsAt,
            command.EndsAt,
            normalizedNotes,
            now);
        transaction.Add(appointment);
        await transaction.CommitAsync(cancellationToken);

        return new(CreateAppointmentStatus.Success, new AppointmentItem(
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

    private static bool IsValid(CreateAppointmentCommand command, string? normalizedNotes) =>
        command.PatientId != Guid.Empty &&
        command.ClinicUnitId != Guid.Empty &&
        command.EndsAt > command.StartsAt &&
        command.EndsAt - command.StartsAt <= Appointment.MaximumDuration &&
        (normalizedNotes is null || normalizedNotes.Length <= Appointment.NotesMaxLength);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CreateAppointmentCommand(
    Guid PatientId,
    Guid ClinicUnitId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Notes);

public enum CreateAppointmentStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    ProfessionalNotFound = 5,
    ClinicUnitNotAvailable = 6,
    PatientNotAssigned = 7,
    StartsInPast = 8,
    ProfessionalConflict = 9,
    PatientConflict = 10,
}

/// <summary>Representa um atendimento agendado retornado pela aplicação.</summary>
public sealed record AppointmentItem(
    Guid Id,
    Guid ClinicUnitId,
    Guid PatientId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateAppointmentResult(
    CreateAppointmentStatus Status,
    AppointmentItem? Appointment = null);
