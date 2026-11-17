using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Scheduling.Contracts;

public interface IAppointmentStore
{
    Task<IAppointmentTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IAppointmentTransaction : IAsyncDisposable
{
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken);

    Task<TenantMembershipRole?> FindActiveMembershipRoleAsync(
        Guid tenantId,
        Guid userId,
        Guid membershipId,
        CancellationToken cancellationToken);

    Task<SchedulingProfessional?> FindActiveProfessionalAsync(
        Guid tenantId,
        Guid userId,
        Guid membershipId,
        CancellationToken cancellationToken);

    Task<bool> IsActiveUnitLinkedAsync(
        Guid tenantId,
        Guid membershipId,
        Guid clinicUnitId,
        CancellationToken cancellationToken);

    Task<bool> IsActivePatientAssignedAsync(
        Guid tenantId,
        Guid professionalId,
        Guid patientId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SchedulingPatientOption>> ListPatientOptionsAsync(
        Guid tenantId,
        Guid professionalId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SchedulingClinicUnitOption>> ListClinicUnitOptionsAsync(
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken);

    Task<AppointmentConflicts> FindConflictsAsync(
        Guid tenantId,
        Guid professionalId,
        Guid patientId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid? excludedAppointmentId,
        CancellationToken cancellationToken);

    Task<Appointment?> FindAppointmentForUpdateAsync(
        Guid tenantId,
        Guid professionalId,
        Guid appointmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AppointmentScheduleItem>> ListAppointmentsAsync(
        Guid tenantId,
        Guid? professionalId,
        Guid? clinicUnitId,
        DateTimeOffset from,
        DateTimeOffset through,
        AppointmentStatus? status,
        CancellationToken cancellationToken);

    Task<AppointmentScheduleItem?> FindAppointmentAsync(
        Guid tenantId,
        Guid? professionalId,
        Guid appointmentId,
        CancellationToken cancellationToken);

    void Add(Appointment appointment);

    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record SchedulingProfessional(Guid Id, Guid MembershipId, int? DefaultAppointmentDurationMinutes);

public sealed record SchedulingPatientOption(Guid Id, string FullName, string? HealthPlan);

public sealed record SchedulingClinicUnitOption(Guid Id, string Name, string TimeZoneId);

public sealed record AppointmentConflicts(bool Professional, bool Patient);

/// <summary>Agendamento com os dados mínimos de paciente e unidade para exibição na agenda.</summary>
public sealed record AppointmentScheduleItem(
    Guid Id,
    Guid ProfessionalId,
    string ProfessionalName,
    Guid PatientId,
    string PatientName,
    Guid ClinicUnitId,
    string ClinicUnitName,
    string TimeZoneId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public decimal DurationMinutes =>
        (EndsAt - StartsAt).Ticks / (decimal)TimeSpan.TicksPerMinute;
}
