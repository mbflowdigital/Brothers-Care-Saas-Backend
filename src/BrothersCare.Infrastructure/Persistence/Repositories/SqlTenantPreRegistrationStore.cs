using System.Data;
using BrothersCare.Application.Tenancy;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlTenantPreRegistrationStore(BrothersCareDbContext dbContext) : ITenantPreRegistrationStore
{
    public async Task<TenantPreRegistrationDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await (from tenant in dbContext.Tenants.AsNoTracking()
               join membership in dbContext.TenantMemberships.AsNoTracking() on tenant.Id equals membership.TenantId
               join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
               where tenant.Id == tenantId && membership.Role == TenantMembershipRole.ClinicAdministrator
               orderby membership.CreatedAt
               select new TenantPreRegistrationDetails(tenant.Id, tenant.Name, tenant.Cnpj ?? string.Empty,
                   user.DisplayName, user.Email, tenant.Status.ToString())).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> CnpjBelongsToAnotherTenantAsync(Guid tenantId, string cnpj, CancellationToken cancellationToken) =>
        dbContext.Tenants.AsNoTracking().AnyAsync(item => item.Id != tenantId && item.Cnpj == cnpj, cancellationToken);

    public async Task<TenantPreRegistrationStatus> UpdateAsync(
        Guid tenantId, TenantPreRegistrationInput input, CancellationToken cancellationToken)
    {
        var invitation = await FindInvitationAsync(tenantId, cancellationToken);
        if (invitation is null) return TenantPreRegistrationStatus.NotFound;
        if (invitation.Tenant.Status != TenantStatus.PendingOnboarding) return TenantPreRegistrationStatus.InvalidState;
        invitation.Tenant.UpdatePendingOnboarding(input.CompanyName, input.Cnpj);
        invitation.User.SetDisplayName(input.ResponsibleName);
        invitation.User.SetEmail(input.ResponsibleEmail);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TenantPreRegistrationStatus.Completed;
    }

    public async Task<TenantPreRegistrationStatus> CancelAsync(
        Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var invitation = await FindInvitationAsync(tenantId, cancellationToken);
        if (invitation is null) return TenantPreRegistrationStatus.NotFound;
        if (invitation.Tenant.Status != TenantStatus.PendingOnboarding) return TenantPreRegistrationStatus.InvalidState;
        invitation.Tenant.CancelPendingOnboarding();
        invitation.Membership.Deactivate();
        var tokens = await dbContext.AccountActionTokens.Where(item => item.TenantMembershipId == invitation.Membership.Id &&
            item.Purpose == AccountActionTokenPurpose.CommercialOnboarding && item.ConsumedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TenantPreRegistrationStatus.Completed;
    }

    public async Task<TenantPreRegistrationStatus> DeleteAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return TenantPreRegistrationStatus.NotFound;
        if (tenant.Status is not (TenantStatus.PendingOnboarding or TenantStatus.Deactivated) ||
            await dbContext.ClinicUnits.AnyAsync(item => item.TenantId == tenantId, cancellationToken))
        {
            return TenantPreRegistrationStatus.InvalidState;
        }

        var memberships = await dbContext.TenantMemberships
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var membershipIds = memberships.Select(item => item.Id).ToArray();
        var tokens = await dbContext.AccountActionTokens
            .Where(item => item.TenantMembershipId != null && membershipIds.Contains(item.TenantMembershipId.Value))
            .ToListAsync(cancellationToken);
        var unitMemberships = await dbContext.ClinicUnitMemberships
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        dbContext.AccountActionTokens.RemoveRange(tokens);
        dbContext.ClinicUnitMemberships.RemoveRange(unitMemberships);
        dbContext.TenantMemberships.RemoveRange(memberships);
        dbContext.Tenants.Remove(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TenantPreRegistrationStatus.Completed;
    }

    private async Task<Invitation?> FindInvitationAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await (from tenant in dbContext.Tenants
               join membership in dbContext.TenantMemberships on tenant.Id equals membership.TenantId
               join user in dbContext.Users on membership.UserId equals user.Id
               where tenant.Id == tenantId && membership.Role == TenantMembershipRole.ClinicAdministrator
               orderby membership.CreatedAt
               select new Invitation(tenant, membership, user)).FirstOrDefaultAsync(cancellationToken);

    private sealed record Invitation(Tenant Tenant, TenantMembership Membership, User User);
}
