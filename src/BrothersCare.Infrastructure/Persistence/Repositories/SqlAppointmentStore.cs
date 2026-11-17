using System.Data;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlAppointmentStore(BrothersCareDbContext dbContext) : IAppointmentStore
{
    public async Task<IAppointmentTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new AppointmentTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class AppointmentTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IAppointmentTransaction
    {
        public async Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var session = await context.UserSessions.SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
            if (session is not null)
            {
                await context.Entry(session).ReloadAsync(cancellationToken);
            }

            return session;
        }

        public Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(
                item => item.UserId == userId && item.Status == UserCredentialStatus.Active,
                cancellationToken);

        public async Task<TenantMembershipRole?> FindActiveMembershipRoleAsync(
            Guid tenantId,
            Guid userId,
            Guid membershipId,
            CancellationToken cancellationToken)
        {
            var role = await (from membership in context.TenantMemberships.AsNoTracking()
                              join tenant in context.Tenants.AsNoTracking()
                                  on membership.TenantId equals tenant.Id
                              where membership.TenantId == tenantId && membership.Id == membershipId &&
                                  membership.UserId == userId && membership.Status == MembershipStatus.Active &&
                                  tenant.Status == TenantStatus.Active
                              select (TenantMembershipRole?)membership.Role)
                .SingleOrDefaultAsync(cancellationToken);
            return role;
        }

        public Task<SchedulingProfessional?> FindActiveProfessionalAsync(
            Guid tenantId,
            Guid userId,
            Guid membershipId,
            CancellationToken cancellationToken) =>
            (from professional in context.Professionals.AsNoTracking()
             join membership in context.TenantMemberships.AsNoTracking()
                 on new { professional.TenantId, MembershipId = professional.TenantMembershipId }
                 equals new { membership.TenantId, MembershipId = (Guid?)membership.Id }
             join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
             where professional.TenantId == tenantId && professional.TenantMembershipId == membershipId &&
                 professional.Status == ProfessionalStatus.Active && membership.Id == membershipId &&
                 membership.UserId == userId && membership.Status == MembershipStatus.Active &&
                 tenant.Status == TenantStatus.Active
             select new SchedulingProfessional(
                 professional.Id,
                 membership.Id,
                 professional.DefaultAppointmentDurationMinutes))
            .SingleOrDefaultAsync(cancellationToken);

        public Task<SchedulingProfessional?> FindActiveProfessionalByIdAsync(
            Guid tenantId,
            Guid professionalId,
            CancellationToken cancellationToken) =>
            (from professional in context.Professionals.AsNoTracking()
             join membership in context.TenantMemberships.AsNoTracking()
                 on new { professional.TenantId, MembershipId = professional.TenantMembershipId }
                 equals new { membership.TenantId, MembershipId = (Guid?)membership.Id }
             join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
             where professional.TenantId == tenantId && professional.Id == professionalId &&
                 professional.Status == ProfessionalStatus.Active &&
                 membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
             select new SchedulingProfessional(
                 professional.Id,
                 membership.Id,
                 professional.DefaultAppointmentDurationMinutes))
            .SingleOrDefaultAsync(cancellationToken);

        public Task<bool> IsActiveProfessionalUnitLinkedAsync(
            Guid tenantId,
            Guid professionalId,
            Guid clinicUnitId,
            CancellationToken cancellationToken) =>
            (from professional in context.Professionals.AsNoTracking()
             join membership in context.TenantMemberships.AsNoTracking()
                 on new { professional.TenantId, MembershipId = professional.TenantMembershipId }
                 equals new { membership.TenantId, MembershipId = (Guid?)membership.Id }
             join link in context.ClinicUnitMemberships.AsNoTracking()
                 on new { membership.TenantId, MembershipId = membership.Id }
                 equals new { link.TenantId, MembershipId = link.TenantMembershipId }
             join unit in context.ClinicUnits.AsNoTracking()
                 on new { link.TenantId, ClinicUnitId = link.ClinicUnitId }
                 equals new { unit.TenantId, ClinicUnitId = unit.Id }
             where professional.TenantId == tenantId && professional.Id == professionalId &&
                 professional.Status == ProfessionalStatus.Active && membership.Status == MembershipStatus.Active &&
                 unit.Id == clinicUnitId && unit.IsActive
             select link.Id).AnyAsync(cancellationToken);

        public Task<bool> IsActiveUnitLinkedAsync(
            Guid tenantId,
            Guid membershipId,
            Guid clinicUnitId,
            CancellationToken cancellationToken) =>
            (from link in context.ClinicUnitMemberships.AsNoTracking()
             join unit in context.ClinicUnits.AsNoTracking()
                 on new { link.TenantId, ClinicUnitId = link.ClinicUnitId }
                 equals new { unit.TenantId, ClinicUnitId = unit.Id }
             where link.TenantId == tenantId && link.TenantMembershipId == membershipId &&
                 unit.Id == clinicUnitId && unit.IsActive
             select link.Id).AnyAsync(cancellationToken);

        public Task<bool> IsActivePatientAssignedAsync(
            Guid tenantId,
            Guid professionalId,
            Guid patientId,
            CancellationToken cancellationToken) =>
            (from assignment in context.PatientCareAssignments.AsNoTracking()
             join patient in context.Patients.AsNoTracking()
                 on new { assignment.TenantId, PatientId = assignment.PatientId }
                 equals new { patient.TenantId, PatientId = patient.Id }
             where assignment.TenantId == tenantId && assignment.PatientId == patientId &&
                 assignment.ProfessionalId == professionalId && assignment.Status == CareAssignmentStatus.Active &&
                 patient.Status == PatientStatus.Active
             select assignment.Id).AnyAsync(cancellationToken);

        public async Task<bool> IsProfessionalAvailableAsync(
            Guid tenantId,
            Guid professionalId,
            Guid clinicUnitId,
            DateTimeOffset startsAt,
            DateTimeOffset endsAt,
            CancellationToken cancellationToken)
        {
            var timeZoneId = await context.ClinicUnits.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Id == clinicUnitId && item.IsActive)
                .Select(item => item.TimeZoneId)
                .SingleOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                return false;
            }

            var hasBlock = await context.ProfessionalAvailabilityBlocks.AsNoTracking().AnyAsync(item =>
                item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                item.StartsAt < endsAt && item.EndsAt > startsAt, cancellationToken);
            if (hasBlock)
            {
                return false;
            }

            var windows = await context.ProfessionalAvailabilityWindows.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                    item.ClinicUnitId == clinicUnitId)
                .Select(item => new AvailabilityWindow(item.DayOfWeek, item.StartsAt, item.EndsAt))
                .ToArrayAsync(cancellationToken);

            // Sem expediente configurado, preservamos temporariamente a disponibilidade livre dos perfis existentes.
            if (windows.Length == 0)
            {
                return true;
            }

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                return false;
            }

            var localStart = TimeZoneInfo.ConvertTime(startsAt, timeZone);
            var localEnd = TimeZoneInfo.ConvertTime(endsAt, timeZone);
            if (localStart.Date != localEnd.Date)
            {
                return false;
            }

            var startTime = TimeOnly.FromDateTime(localStart.DateTime);
            var endTime = TimeOnly.FromDateTime(localEnd.DateTime);
            return windows.Any(item => item.DayOfWeek == localStart.DayOfWeek &&
                item.StartsAt <= startTime && item.EndsAt >= endTime);
        }

        public async Task<IReadOnlyList<ProfessionalAvailabilityWindow>> ListAvailabilityWindowsAsync(
            Guid tenantId,
            Guid professionalId,
            Guid clinicUnitId,
            CancellationToken cancellationToken) =>
            await context.ProfessionalAvailabilityWindows.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                    item.ClinicUnitId == clinicUnitId)
                .OrderBy(item => item.DayOfWeek).ThenBy(item => item.StartsAt).ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);

        public async Task ReplaceAvailabilityWindowsAsync(
            Guid tenantId,
            Guid professionalId,
            Guid clinicUnitId,
            IReadOnlyList<ProfessionalAvailabilityWindow> windows,
            CancellationToken cancellationToken)
        {
            var current = await context.ProfessionalAvailabilityWindows.Where(item =>
                    item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                    item.ClinicUnitId == clinicUnitId)
                .ToArrayAsync(cancellationToken);
            context.ProfessionalAvailabilityWindows.RemoveRange(current);
            context.ProfessionalAvailabilityWindows.AddRange(windows);
        }

        public Task<bool> HasScheduledAppointmentOverlapAsync(
            Guid tenantId,
            Guid professionalId,
            DateTimeOffset startsAt,
            DateTimeOffset endsAt,
            CancellationToken cancellationToken) =>
            context.Appointments.AsNoTracking().AnyAsync(item =>
                item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                item.Status == AppointmentStatus.Scheduled &&
                item.StartsAt < endsAt && item.EndsAt > startsAt, cancellationToken);

        public async Task<IReadOnlyList<ProfessionalAvailabilityBlock>> ListAvailabilityBlocksAsync(
            Guid tenantId,
            Guid professionalId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            await context.ProfessionalAvailabilityBlocks.AsNoTracking().Where(item =>
                    item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                    item.StartsAt < to && item.EndsAt > from)
                .OrderBy(item => item.StartsAt).ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);

        public Task<ProfessionalAvailabilityBlock?> FindAvailabilityBlockAsync(
            Guid tenantId,
            Guid professionalId,
            Guid blockId,
            CancellationToken cancellationToken) =>
            context.ProfessionalAvailabilityBlocks.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId && item.ProfessionalId == professionalId && item.Id == blockId,
                cancellationToken);

        public void AddAvailabilityBlock(ProfessionalAvailabilityBlock block) =>
            context.ProfessionalAvailabilityBlocks.Add(block);

        public void RemoveAvailabilityBlock(ProfessionalAvailabilityBlock block) =>
            context.ProfessionalAvailabilityBlocks.Remove(block);

        public async Task<IReadOnlyList<SchedulingPatientOption>> ListPatientOptionsAsync(
            Guid tenantId,
            Guid professionalId,
            CancellationToken cancellationToken) =>
            await (from assignment in context.PatientCareAssignments.AsNoTracking()
                   join patient in context.Patients.AsNoTracking()
                       on new { assignment.TenantId, PatientId = assignment.PatientId }
                       equals new { patient.TenantId, PatientId = patient.Id }
                   where assignment.TenantId == tenantId && assignment.ProfessionalId == professionalId &&
                       assignment.Status == CareAssignmentStatus.Active && patient.Status == PatientStatus.Active
                   orderby patient.FullName, patient.Id
                   select new SchedulingPatientOption(patient.Id, patient.FullName, patient.HealthPlan))
                .ToListAsync(cancellationToken);

        private sealed record AvailabilityWindow(DayOfWeek DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt);

        public async Task<IReadOnlyList<SchedulingClinicUnitOption>> ListClinicUnitOptionsAsync(
            Guid tenantId,
            Guid membershipId,
            CancellationToken cancellationToken) =>
            await (from link in context.ClinicUnitMemberships.AsNoTracking()
                   join unit in context.ClinicUnits.AsNoTracking()
                       on new { link.TenantId, ClinicUnitId = link.ClinicUnitId }
                       equals new { unit.TenantId, ClinicUnitId = unit.Id }
                   where link.TenantId == tenantId && link.TenantMembershipId == membershipId && unit.IsActive
                   orderby unit.Name, unit.Id
                   select new SchedulingClinicUnitOption(unit.Id, unit.Name, unit.TimeZoneId))
                .ToListAsync(cancellationToken);

        public async Task<AppointmentConflicts> FindConflictsAsync(
            Guid tenantId,
            Guid professionalId,
            Guid patientId,
            DateTimeOffset startsAt,
            DateTimeOffset endsAt,
            Guid? excludedAppointmentId,
            CancellationToken cancellationToken)
        {
            var activeOverlaps = context.Appointments.AsNoTracking().Where(item =>
                item.TenantId == tenantId && item.Status == AppointmentStatus.Scheduled &&
                (excludedAppointmentId == null || item.Id != excludedAppointmentId) &&
                item.StartsAt < endsAt && item.EndsAt > startsAt);
            var professionalConflict = await activeOverlaps.AnyAsync(
                item => item.ProfessionalId == professionalId,
                cancellationToken);
            var patientConflict = await activeOverlaps.AnyAsync(
                item => item.PatientId == patientId,
                cancellationToken);
            return new(professionalConflict, patientConflict);
        }

        public async Task<IReadOnlyList<AppointmentConflictWindow>> ListSchedulingConflictWindowsAsync(
            Guid tenantId,
            Guid professionalId,
            Guid patientId,
            DateTimeOffset from,
            DateTimeOffset through,
            Guid? excludedAppointmentId,
            CancellationToken cancellationToken) =>
            await context.Appointments.AsNoTracking()
                .Where(item => item.TenantId == tenantId &&
                    item.Status == AppointmentStatus.Scheduled &&
                    (excludedAppointmentId == null || item.Id != excludedAppointmentId) &&
                    (item.ProfessionalId == professionalId || item.PatientId == patientId) &&
                    item.StartsAt < through && item.EndsAt > from)
                .Select(item => new AppointmentConflictWindow(
                    item.Id, item.ProfessionalId, item.PatientId, item.StartsAt, item.EndsAt))
                .ToArrayAsync(cancellationToken);

        public Task<Appointment?> FindAppointmentForUpdateAsync(
            Guid tenantId,
            Guid professionalId,
            Guid appointmentId,
            CancellationToken cancellationToken) =>
            context.Appointments.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId && item.ProfessionalId == professionalId && item.Id == appointmentId,
                cancellationToken);

        public async Task<IReadOnlyList<AppointmentScheduleItem>> ListAppointmentsAsync(
            Guid tenantId,
            Guid? professionalId,
            Guid? clinicUnitId,
            DateTimeOffset from,
            DateTimeOffset through,
            AppointmentStatus? status,
            CancellationToken cancellationToken)
        {
            var appointments = context.Appointments.AsNoTracking().Where(item =>
                item.TenantId == tenantId &&
                (professionalId == null || item.ProfessionalId == professionalId) &&
                (clinicUnitId == null || item.ClinicUnitId == clinicUnitId) &&
                item.StartsAt >= from && item.StartsAt <= through &&
                (status == null || item.Status == status));
            var orderedAppointments = appointments
                .OrderBy(item => item.StartsAt)
                .ThenBy(item => item.Id);
            var rows = await BuildScheduleRows(orderedAppointments)
                .ToListAsync(cancellationToken);
            return rows.Select(ToScheduleItem).ToArray();
        }

        public async Task<AppointmentScheduleItem?> FindAppointmentAsync(
            Guid tenantId,
            Guid? professionalId,
            Guid appointmentId,
            CancellationToken cancellationToken)
        {
            var appointments = context.Appointments.AsNoTracking().Where(item =>
                item.TenantId == tenantId &&
                (professionalId == null || item.ProfessionalId == professionalId) &&
                item.Id == appointmentId);
            var row = await BuildScheduleRows(appointments).SingleOrDefaultAsync(cancellationToken);
            return row is null ? null : ToScheduleItem(row);
        }

        public void Add(Appointment appointment) => context.Appointments.Add(appointment);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();

        private IQueryable<AppointmentScheduleRow> BuildScheduleRows(IQueryable<Appointment> appointments) =>
            from appointment in appointments
            join patient in context.Patients.AsNoTracking()
                on new { appointment.TenantId, PatientId = appointment.PatientId }
                equals new { patient.TenantId, PatientId = patient.Id }
            join unit in context.ClinicUnits.AsNoTracking()
                on new { appointment.TenantId, ClinicUnitId = appointment.ClinicUnitId }
                equals new { unit.TenantId, ClinicUnitId = unit.Id }
            join professional in context.Professionals.AsNoTracking()
                on new { appointment.TenantId, appointment.ProfessionalId }
                equals new { professional.TenantId, ProfessionalId = professional.Id }
            select new AppointmentScheduleRow(
                appointment.Id,
                professional.Id,
                professional.FullName,
                patient.Id,
                patient.FullName,
                unit.Id,
                unit.Name,
                unit.TimeZoneId,
                appointment.StartsAt,
                appointment.EndsAt,
                appointment.Notes,
                appointment.Status,
                appointment.CreatedAt,
                appointment.UpdatedAt);

        private static AppointmentScheduleItem ToScheduleItem(AppointmentScheduleRow row) =>
            new(
                row.Id,
                row.ProfessionalId,
                row.ProfessionalName,
                row.PatientId,
                row.PatientName,
                row.ClinicUnitId,
                row.ClinicUnitName,
                row.TimeZoneId,
                row.StartsAt,
                row.EndsAt,
                row.Notes,
                row.Status.ToString(),
                row.CreatedAt,
                row.UpdatedAt);

        private sealed record AppointmentScheduleRow(
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
            AppointmentStatus Status,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt);
    }
}
