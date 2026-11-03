using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlCommercialOnboardingSource(BrothersCareDbContext dbContext) : ICommercialOnboardingSource
{
    public async Task<CommercialOnboardingLookup> FindAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        var token = await dbContext.AccountActionTokens.AsNoTracking().SingleOrDefaultAsync(
            item => item.SecretHash == tokenHash &&
                    item.Purpose == AccountActionTokenPurpose.CommercialOnboarding,
            cancellationToken);
        if (token is null)
        {
            return new(CommercialOnboardingLookupStatus.Invalid);
        }

        if (token.ConsumedAt is not null)
        {
            return new(CommercialOnboardingLookupStatus.Used);
        }

        if (token.RevokedAt is not null)
        {
            return new(CommercialOnboardingLookupStatus.Revoked);
        }

        if (token.TenantMembershipId is not { } membershipId)
        {
            return new(CommercialOnboardingLookupStatus.Invalid);
        }

        var context = await (
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            join tenant in dbContext.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            where membership.Id == membershipId &&
                  membership.UserId == token.UserId &&
                  membership.Role == TenantMembershipRole.ClinicAdministrator &&
                  membership.Status == MembershipStatus.Invited &&
                  tenant.Status == TenantStatus.PendingOnboarding &&
                  tenant.Cnpj != null
            select new CommercialOnboardingRecord(
                tenant.Id,
                user.Email,
                tenant.Cnpj!,
                tenant.Name,
                token.ExpiresAt))
            .SingleOrDefaultAsync(cancellationToken);

        return context is null
            ? new(CommercialOnboardingLookupStatus.Invalid)
            : new(CommercialOnboardingLookupStatus.Ready, context);
    }
}
