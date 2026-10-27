using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity;

public sealed class PlatformAdministratorBootstrapper(
    IPlatformAdministratorStore store,
    TimeProvider timeProvider,
    IAuthenticationAudit audit) : IPlatformAdministratorBootstrapper
{
    public async Task<PlatformAdministratorBootstrapResult> EnsureSuperAdministratorAsync(
        PlatformAdministratorBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        await using var transaction = await store.BeginAsync(cancellationToken);
        var user = await transaction.FindUserAsync(normalizedEmail, cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (user is null)
        {
            user = new User(Guid.NewGuid(), request.Email, request.DisplayName, now);
            transaction.AddUser(user);
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }

        var existingAdministrator = await transaction.FindAdministratorAsync(user.Id, cancellationToken);

        if (existingAdministrator is not null)
        {
            if (existingAdministrator.Status == PlatformAdministratorStatus.Inactive)
            {
                throw new InvalidOperationException("The platform administrator is inactive and must be reactivated through an audited workflow.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new PlatformAdministratorBootstrapResult(false);
        }

        transaction.AddAdministrator(
            new PlatformAdministrator(
                Guid.NewGuid(),
                user.Id,
                PlatformAdministratorRole.SuperAdministrator,
                now));

        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.PlatformAdministratorCreated, now);
        return new PlatformAdministratorBootstrapResult(true);
    }
}
