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

    Task<SchedulingProfessional?> FindActiveProfessionalByIdAsync(
        Guid tenantId,
        Guid professionalId,
        CancellationToken cancellationToken);

    Task<bool> IsActiveProfessionalUnitLinkedAsync(
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
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

    Task<bool> IsProfessionalAvailableAsync(
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfessionalAvailabilityWindow>> ListAvailabilityWindowsAsync(
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
        CancellationToken cancellationToken);

    Task ReplaceAvailabilityWindowsAsync(
        Guid tenantId,
        Guid professionalId,
        Guid clinicUnitId,
        IReadOnlyList<ProfessionalAvailabilityWindow> windows,
        CancellationToken cancellationToken);

    Task<bool> HasScheduledAppointmentOverlapAsync(
        Guid tenantId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfessionalAvailabilityBlock>> ListAvailabilityBlocksAsync(
        Guid tenantId,
        Guid professionalId,
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellationToken);

    Task<ProfessionalAvailabilityBlock?> FindAvailabilityBlockAsync(
        Guid tenantId,
        Guid professionalId,
        Guid blockId,
        CancellationToken cancellationToken);

    void AddAvailabilityBlock(ProfessionalAvailabilityBlock block);

    void RemoveAvailabilityBlock(ProfessionalAvailabilityBlock block);

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

    Task<IReadOnlyList<AppointmentConflictWindow>> ListSchedulingConflictWindowsAsync(
        Guid tenantId,
        Guid professionalId,
        Guid patientId,
        DateTimeOffset from,
        DateTimeOffset through,
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

public sealed record AppointmentConflictWindow(
    Guid AppointmentId,
    Guid ProfessionalId,
    Guid PatientId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

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
