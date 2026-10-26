using BrothersCare.Application.Identity;
using BrothersCare.Domain.Identity;
using BrothersCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Identity;

public sealed class PlatformAdministratorBootstrapper(BrothersCareDbContext dbContext) : IPlatformAdministratorBootstrapper
{
    public async Task<PlatformAdministratorBootstrapResult> EnsureSuperAdministratorAsync(
        PlatformAdministratorBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (user is null)
        {
            user = new User(Guid.NewGuid(), request.Email, request.DisplayName, now);
            dbContext.Users.Add(user);
        }

        var existingAdministrator = await dbContext.PlatformAdministrators.SingleOrDefaultAsync(
            item => item.UserId == user.Id,
            cancellationToken);

        if (existingAdministrator is not null)
        {
            if (existingAdministrator.Status == PlatformAdministratorStatus.Inactive)
            {
                throw new InvalidOperationException("The platform administrator is inactive and must be reactivated through an audited workflow.");
            }

            return new PlatformAdministratorBootstrapResult(false);
        }

        dbContext.PlatformAdministrators.Add(
            new PlatformAdministrator(
                Guid.NewGuid(),
                user.Id,
                PlatformAdministratorRole.SuperAdministrator,
                now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return new PlatformAdministratorBootstrapResult(true);
    }
}
