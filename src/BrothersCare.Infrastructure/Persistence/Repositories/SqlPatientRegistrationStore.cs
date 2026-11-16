using System.Data;
using BrothersCare.Application.Care.Contracts;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlPatientRegistrationStore(BrothersCareDbContext dbContext) : IPatientRegistrationStore
{
    public async Task<IPatientRegistrationTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new PatientRegistrationTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class PatientRegistrationTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IPatientRegistrationTransaction
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
            context.UserCredentials.AnyAsync(item => item.UserId == userId && item.Status == UserCredentialStatus.Active,
                cancellationToken);

        public Task<bool> HasActiveClinicAdministratorAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken cancellationToken) =>
            (from membership in context.TenantMemberships
             join tenant in context.Tenants on membership.TenantId equals tenant.Id
             where membership.TenantId == tenantId && membership.UserId == userId &&
                 membership.Status == MembershipStatus.Active &&
                 membership.Role == TenantMembershipRole.ClinicAdministrator &&
                 tenant.Status == TenantStatus.Active
             select membership.Id).AnyAsync(cancellationToken);

        public Task<PatientAssignmentTarget?> FindAssignmentTargetAsync(
            Guid tenantId,
            Guid professionalId,
            Guid clinicUnitId,
            CancellationToken cancellationToken) =>
            (from professional in context.Professionals.AsNoTracking()
             join membership in context.TenantMemberships.AsNoTracking()
                 on new { professional.TenantId, MembershipId = professional.TenantMembershipId }
                 equals new { membership.TenantId, MembershipId = (Guid?)membership.Id }
             join unitMembership in context.ClinicUnitMemberships.AsNoTracking()
                 on new { membership.TenantId, MembershipId = membership.Id }
                 equals new { unitMembership.TenantId, MembershipId = unitMembership.TenantMembershipId }
             join unit in context.ClinicUnits.AsNoTracking()
                 on new { unitMembership.TenantId, UnitId = unitMembership.ClinicUnitId }
                 equals new { unit.TenantId, UnitId = unit.Id }
             where professional.TenantId == tenantId && professional.Id == professionalId &&
                 professional.Status == ProfessionalStatus.Active &&
                 membership.Status == MembershipStatus.Active && unit.Id == clinicUnitId && unit.IsActive
             select new PatientAssignmentTarget(professional.Id, unit.Id)).SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<PatientProfessionalOption>> ListProfessionalOptionsAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            var professionals = await context.Professionals.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Status == ProfessionalStatus.Active &&
                    item.TenantMembershipId != null)
                .OrderBy(item => item.FullName)
                .ToListAsync(cancellationToken);
            var membershipIds = professionals.Select(item => item.TenantMembershipId!.Value).ToArray();
            var unitLinks = await (from link in context.ClinicUnitMemberships.AsNoTracking()
                                   join unit in context.ClinicUnits.AsNoTracking()
                                       on new { link.TenantId, UnitId = link.ClinicUnitId }
                                       equals new { unit.TenantId, UnitId = unit.Id }
                                   where link.TenantId == tenantId && unit.IsActive &&
                                       membershipIds.Contains(link.TenantMembershipId)
                                   select new { link.TenantMembershipId, unit.Id }).ToListAsync(cancellationToken);
            return professionals.Select(professional => new PatientProfessionalOption(
                professional.Id,
                professional.FullName,
                professional.Specialty,
                unitLinks.Where(link => link.TenantMembershipId == professional.TenantMembershipId)
                    .Select(link => link.Id).ToArray())).ToArray();
        }

        public async Task<IReadOnlyList<PatientClinicUnitOption>> ListClinicUnitOptionsAsync(
            Guid tenantId,
            CancellationToken cancellationToken) =>
            await context.ClinicUnits.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.IsActive)
                .OrderBy(item => item.Name)
                .Select(item => new PatientClinicUnitOption(item.Id, item.Name, item.TimeZoneId))
                .ToListAsync(cancellationToken);

        public async Task<PatientPage> ListAsync(
            Guid tenantId,
            Guid? professionalId,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var query = context.Patients.AsNoTracking().Where(item => item.TenantId == tenantId);
            if (professionalId is { } assignedProfessionalId)
            {
                query = query.Where(patient => context.PatientCareAssignments.Any(assignment =>
                    assignment.TenantId == tenantId && assignment.PatientId == patient.Id &&
                    assignment.ProfessionalId == assignedProfessionalId &&
                    assignment.Status == CareAssignmentStatus.Active));
            }
            if (search is not null)
            {
                query = query.Where(item => item.FullName.Contains(search) ||
                    (item.HealthPlan != null && item.HealthPlan.Contains(search)));
            }

            var totalItems = await query.CountAsync(cancellationToken);
            var patients = await query.OrderBy(item => item.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            var patientIds = patients.Select(item => item.Id).ToArray();
            var assignments = await (from assignment in context.PatientCareAssignments.AsNoTracking()
                                     join professional in context.Professionals.AsNoTracking()
                                         on new { assignment.TenantId, ProfessionalId = assignment.ProfessionalId }
                                         equals new { professional.TenantId, ProfessionalId = professional.Id }
                                     where assignment.TenantId == tenantId &&
                                         assignment.Status == CareAssignmentStatus.Active &&
                                         patientIds.Contains(assignment.PatientId)
                                     select new
                                     {
                                         assignment.PatientId,
                                         ProfessionalId = (Guid?)professional.Id,
                                         ProfessionalName = professional.FullName,
                                         ClinicUnitId = (Guid?)assignment.ClinicUnitId,
                                     }).ToListAsync(cancellationToken);

            var items = patients.Select(patient =>
            {
                var assignment = assignments.SingleOrDefault(item => item.PatientId == patient.Id);
                return new PatientListItem(
                    patient.Id,
                    patient.FullName,
                    patient.BirthDate,
                    patient.HealthPlan,
                    patient.Status.ToString(),
                    assignment?.ProfessionalId,
                    assignment?.ProfessionalName,
                    assignment?.ClinicUnitId,
                    patient.CreatedAt);
            }).ToArray();
            var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
            return new(items, page, pageSize, totalItems, totalPages);
        }

        public Task<Guid?> FindActiveProfessionalIdAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken cancellationToken) =>
            (from membership in context.TenantMemberships.AsNoTracking()
             join professional in context.Professionals.AsNoTracking()
                 on new { membership.TenantId, MembershipId = (Guid?)membership.Id }
                 equals new { professional.TenantId, MembershipId = professional.TenantMembershipId }
             join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
             where membership.TenantId == tenantId && membership.UserId == userId &&
                 membership.Status == MembershipStatus.Active &&
                 membership.Role == TenantMembershipRole.Professional &&
                 professional.Status == ProfessionalStatus.Active && tenant.Status == TenantStatus.Active
             select (Guid?)professional.Id).SingleOrDefaultAsync(cancellationToken);

        public Task<Patient?> FindPatientAsync(
            Guid tenantId,
            Guid patientId,
            CancellationToken cancellationToken) =>
            context.Patients.SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == patientId,
                cancellationToken);

        public Task<PatientCareAssignment?> FindActiveAssignmentAsync(
            Guid tenantId,
            Guid patientId,
            CancellationToken cancellationToken) =>
            context.PatientCareAssignments.SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.PatientId == patientId &&
                    item.Status == CareAssignmentStatus.Active,
                cancellationToken);

        public void Add(Patient patient) => context.Patients.Add(patient);

        public void Add(PatientCareAssignment assignment) => context.PatientCareAssignments.Add(assignment);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
