using System.Data;
using BrothersCare.Application.Tenancy;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlClinicUnitStore(BrothersCareDbContext dbContext) : IClinicUnitStore
{
    public async Task<IClinicUnitTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new ClinicUnitTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class ClinicUnitTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IClinicUnitTransaction
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
            context.UserCredentials.AnyAsync(item => item.UserId == userId && item.Status == UserCredentialStatus.Active, cancellationToken);

        public Task<bool> HasActiveClinicAdministratorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
            (from membership in context.TenantMemberships
             join tenant in context.Tenants on membership.TenantId equals tenant.Id
             where membership.TenantId == tenantId && membership.UserId == userId &&
                 membership.Status == MembershipStatus.Active &&
                 membership.Role == TenantMembershipRole.ClinicAdministrator &&
                 tenant.Status == TenantStatus.Active
             select membership.Id).AnyAsync(cancellationToken);

        public async Task<ClinicUnitPage> ListAsync(
            Guid tenantId,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var query = context.ClinicUnits.AsNoTracking().Where(item => item.TenantId == tenantId);
            if (search is not null)
            {
                query = query.Where(item => item.Name.Contains(search));
            }

            var totalItems = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(item => new ClinicUnitItem(item.Id, item.Name, item.IsActive, item.CreatedAt))
                .ToListAsync(cancellationToken);
            return new(items, page, pageSize, totalItems,
                totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize));
        }

        public Task<ClinicUnitDetails?> FindAsync(Guid tenantId, Guid clinicUnitId, CancellationToken cancellationToken) =>
            context.ClinicUnits.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Id == clinicUnitId)
                .Select(item => new ClinicUnitDetails(item.Id, item.Name, item.IsActive, item.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken);

        public Task<ClinicUnit?> FindTrackedAsync(Guid tenantId, Guid clinicUnitId, CancellationToken cancellationToken) =>
            context.ClinicUnits.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == clinicUnitId, cancellationToken);

        public async Task<IReadOnlyList<Appointment>> FindFutureScheduledAppointmentsAsync(
            Guid tenantId,
            Guid clinicUnitId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            await context.Appointments.Where(item => item.TenantId == tenantId && item.ClinicUnitId == clinicUnitId &&
                    item.Status == AppointmentStatus.Scheduled && item.StartsAt > now)
                .ToListAsync(cancellationToken);

        public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? exceptClinicUnitId, CancellationToken cancellationToken)
        {
            var query = context.ClinicUnits.Where(item => item.TenantId == tenantId && item.Name == name);
            return exceptClinicUnitId is { } clinicUnitId
                ? query.AnyAsync(item => item.Id != clinicUnitId, cancellationToken)
                : query.AnyAsync(cancellationToken);
        }

        public void Add(ClinicUnit clinicUnit) => context.ClinicUnits.Add(clinicUnit);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
