using System.Data;
using BrothersCare.Application.Care.Contracts;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlClinicalRecordStore(BrothersCareDbContext dbContext) : IClinicalRecordStore
{
    public async Task<IClinicalRecordTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new Transaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));

    private sealed class Transaction(BrothersCareDbContext context, IDbContextTransaction transaction)
        : IClinicalRecordTransaction
    {
        public async Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var session = await context.UserSessions.SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
            if (session is not null) await context.Entry(session).ReloadAsync(cancellationToken);
            return session;
        }

        public Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.AnyAsync(item => item.UserId == userId &&
                item.Status == UserCredentialStatus.Active, cancellationToken);

        public Task<ClinicalAuthorContext?> FindAuthorContextAsync(
            Guid tenantId, Guid userId, Guid membershipId, CancellationToken cancellationToken) =>
            (from membership in context.TenantMemberships.AsNoTracking()
             join professional in context.Professionals.AsNoTracking()
                 on new { membership.TenantId, MembershipId = (Guid?)membership.Id }
                 equals new { professional.TenantId, MembershipId = professional.TenantMembershipId }
             join tenant in context.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
             where membership.TenantId == tenantId && membership.Id == membershipId &&
                 membership.UserId == userId && membership.Status == MembershipStatus.Active &&
                 professional.Status == ProfessionalStatus.Active && tenant.Status == TenantStatus.Active
             select new ClinicalAuthorContext(membership.Id, professional.Id,
                 professional.FullName, professional.Specialty)).SingleOrDefaultAsync(cancellationToken);

        public Task<bool> HasCareAccessAsync(
            Guid tenantId, Guid professionalId, Guid patientId, Guid clinicUnitId,
            CancellationToken cancellationToken) =>
            (from assignment in context.PatientCareAssignments.AsNoTracking()
             join patient in context.Patients.AsNoTracking()
                 on new { assignment.TenantId, PatientId = assignment.PatientId }
                 equals new { patient.TenantId, PatientId = patient.Id }
             join unit in context.ClinicUnits.AsNoTracking()
                 on new { assignment.TenantId, ClinicUnitId = assignment.ClinicUnitId }
                 equals new { unit.TenantId, ClinicUnitId = unit.Id }
             where assignment.TenantId == tenantId && assignment.ProfessionalId == professionalId &&
                 assignment.PatientId == patientId && assignment.ClinicUnitId == clinicUnitId &&
                 assignment.Status == CareAssignmentStatus.Active && patient.Status == PatientStatus.Active &&
                 unit.IsActive
             select assignment.Id).AnyAsync(cancellationToken);

        public Task<ClinicalRecord?> FindTrackedAsync(
            Guid tenantId, Guid recordId, CancellationToken cancellationToken) =>
            context.ClinicalRecords.SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.Id == recordId, cancellationToken);

        public async Task<ClinicalRecordDetails?> GetAsync(
            Guid tenantId, Guid recordId, CancellationToken cancellationToken)
        {
            var item = await DetailsQuery(tenantId, recordId).SingleOrDefaultAsync(cancellationToken);
            return item is null ? null : new ClinicalRecordDetails(item.Id, item.PatientId, item.PatientName,
                item.ClinicUnitId, item.ClinicUnitName, item.AuthorMembershipId, item.AuthorName,
                item.AuthorSpecialty, item.AppointmentId, item.PatientReport, item.ProceduresAndObservations,
                item.AssessmentAndPlan, item.Status.ToString(), item.CreatedAt, item.UpdatedAt,
                item.FinalizedAt, Convert.ToBase64String(item.Version));
        }

        public async Task<IReadOnlyList<ClinicalRecordSummary>> ListAsync(
            Guid tenantId, Guid professionalId, Guid patientId, CancellationToken cancellationToken)
        {
            var records = await (from record in context.ClinicalRecords.AsNoTracking()
                                 join patient in context.Patients.AsNoTracking()
                                     on new { record.TenantId, PatientId = record.PatientId }
                                     equals new { patient.TenantId, PatientId = patient.Id }
                                 join unit in context.ClinicUnits.AsNoTracking()
                                     on new { record.TenantId, UnitId = record.ClinicUnitId }
                                     equals new { unit.TenantId, UnitId = unit.Id }
                                 join membership in context.TenantMemberships.AsNoTracking()
                                     on new { record.TenantId, MembershipId = record.AuthorMembershipId }
                                     equals new { membership.TenantId, MembershipId = membership.Id }
                                 join author in context.Professionals.AsNoTracking()
                                     on new { membership.TenantId, MembershipId = (Guid?)membership.Id }
                                     equals new { author.TenantId, MembershipId = author.TenantMembershipId }
                                 where record.TenantId == tenantId && record.PatientId == patientId &&
                                     context.PatientCareAssignments.Any(assignment =>
                                         assignment.TenantId == tenantId && assignment.PatientId == patientId &&
                                         assignment.ProfessionalId == professionalId &&
                                         assignment.ClinicUnitId == record.ClinicUnitId &&
                                         assignment.Status == CareAssignmentStatus.Active)
                                 orderby record.CreatedAt descending
                                 select new
                                 {
                                     record.Id,
                                     record.PatientId,
                                     PatientName = patient.FullName,
                                     record.ClinicUnitId,
                                     ClinicUnitName = unit.Name,
                                     AuthorName = author.FullName,
                                     AuthorSpecialty = author.Specialty,
                                     record.Status,
                                     record.PatientReport,
                                     record.ProceduresAndObservations,
                                     record.AssessmentAndPlan,
                                     record.CreatedAt,
                                     record.UpdatedAt,
                                     record.FinalizedAt,
                                 }).ToListAsync(cancellationToken);

            return records.Select(item => new ClinicalRecordSummary(
                item.Id, item.PatientId, item.PatientName, item.ClinicUnitId, item.ClinicUnitName,
                item.AuthorName, item.AuthorSpecialty, item.Status.ToString(),
                Summarize(item.ProceduresAndObservations, item.PatientReport, item.AssessmentAndPlan),
                item.CreatedAt, item.UpdatedAt, item.FinalizedAt)).ToArray();
        }

        public async Task<ClinicalRecordOptions> ListOptionsAsync(
            Guid tenantId, Guid professionalId, CancellationToken cancellationToken)
        {
            var assignments = context.PatientCareAssignments.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.ProfessionalId == professionalId &&
                    item.Status == CareAssignmentStatus.Active);
            var patientLinks = await (from assignment in assignments
                                      join patient in context.Patients.AsNoTracking()
                                          on new { assignment.TenantId, PatientId = assignment.PatientId }
                                          equals new { patient.TenantId, PatientId = patient.Id }
                                      where patient.Status == PatientStatus.Active
                                      orderby patient.FullName
                                      select new { patient.Id, patient.FullName, assignment.ClinicUnitId })
                .ToListAsync(cancellationToken);
            var patients = patientLinks.GroupBy(item => new { item.Id, item.FullName })
                .Select(group => new ClinicalPatientOption(group.Key.Id, group.Key.FullName,
                    group.Select(item => item.ClinicUnitId).Distinct().ToArray()))
                .ToArray();
            var units = await (from assignment in assignments
                               join unit in context.ClinicUnits.AsNoTracking()
                                   on new { assignment.TenantId, UnitId = assignment.ClinicUnitId }
                                   equals new { unit.TenantId, UnitId = unit.Id }
                               where unit.IsActive
                               orderby unit.Name
                               select new ClinicalUnitOption(unit.Id, unit.Name))
                .Distinct().ToListAsync(cancellationToken);
            var author = await (from professional in context.Professionals.AsNoTracking()
                                join membership in context.TenantMemberships.AsNoTracking()
                                    on new { professional.TenantId, MembershipId = professional.TenantMembershipId }
                                    equals new { membership.TenantId, MembershipId = (Guid?)membership.Id }
                                where professional.TenantId == tenantId && professional.Id == professionalId
                                select new ClinicalAuthorContext(membership.Id, professional.Id,
                                    professional.FullName, professional.Specialty))
                .SingleAsync(cancellationToken);
            return new(patients, units, author);
        }

        public void Add(ClinicalRecord record) => context.ClinicalRecords.Add(record);

        public async Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();

        private IQueryable<DetailsProjection> DetailsQuery(Guid tenantId, Guid recordId) =>
            from record in context.ClinicalRecords.AsNoTracking()
            join patient in context.Patients.AsNoTracking()
                on new { record.TenantId, PatientId = record.PatientId }
                equals new { patient.TenantId, PatientId = patient.Id }
            join unit in context.ClinicUnits.AsNoTracking()
                on new { record.TenantId, UnitId = record.ClinicUnitId }
                equals new { unit.TenantId, UnitId = unit.Id }
            join membership in context.TenantMemberships.AsNoTracking()
                on new { record.TenantId, MembershipId = record.AuthorMembershipId }
                equals new { membership.TenantId, MembershipId = membership.Id }
            join author in context.Professionals.AsNoTracking()
                on new { membership.TenantId, MembershipId = (Guid?)membership.Id }
                equals new { author.TenantId, MembershipId = author.TenantMembershipId }
            where record.TenantId == tenantId && record.Id == recordId
            select new DetailsProjection(record.Id, record.PatientId, patient.FullName,
                record.ClinicUnitId, unit.Name, record.AuthorMembershipId, author.FullName,
                author.Specialty, record.AppointmentId, record.PatientReport,
                record.ProceduresAndObservations, record.AssessmentAndPlan, record.Status,
                record.CreatedAt, record.UpdatedAt, record.FinalizedAt, record.Version);

        private static string Summarize(params string[] sections)
        {
            var text = sections.FirstOrDefault(section => !string.IsNullOrWhiteSpace(section)) ?? "Rascunho sem conteúdo clínico.";
            text = text.ReplaceLineEndings(" ").Trim();
            return text.Length <= 180 ? text : string.Concat(text.AsSpan(0, 177), "...");
        }

        private sealed record DetailsProjection(
            Guid Id,
            Guid PatientId,
            string PatientName,
            Guid ClinicUnitId,
            string ClinicUnitName,
            Guid AuthorMembershipId,
            string AuthorName,
            string? AuthorSpecialty,
            Guid? AppointmentId,
            string PatientReport,
            string ProceduresAndObservations,
            string AssessmentAndPlan,
            ClinicalRecordStatus Status,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt,
            DateTimeOffset? FinalizedAt,
            byte[] Version);
    }
}
