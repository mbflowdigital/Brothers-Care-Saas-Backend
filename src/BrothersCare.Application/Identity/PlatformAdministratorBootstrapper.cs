using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity;

public sealed class PlatformAdministratorBootstrapper(
    IPlatformAdministratorStore store,
    TimeProvider timeProvider,
    IAuthenticationAudit audit,
    IPasswordHasher passwordHasher) : IPlatformAdministratorBootstrapper
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
        var configuredCpf = string.IsNullOrWhiteSpace(request.Cpf) ? null : User.NormalizeCpf(request.Cpf);
        var user = configuredCpf is null
            ? await transaction.FindUserAsync(normalizedEmail, cancellationToken)
            : await transaction.FindUserByCpfAsync(configuredCpf, cancellationToken) ??
              await transaction.FindUserAsync(normalizedEmail, cancellationToken);
        UserCredential? credential = null;

        var now = timeProvider.GetUtcNow();
        if (user is null)
        {
            user = new User(Guid.NewGuid(), request.Email, request.DisplayName, now);
            transaction.AddUser(user);
            credential = new UserCredential(Guid.NewGuid(), user.Id, now);
            transaction.AddCredential(credential);
        }

        if (!string.IsNullOrWhiteSpace(request.Cpf) && !string.IsNullOrEmpty(request.InitialPassword))
        {
            user.SetCpf(request.Cpf);
            credential ??= await transaction.FindCredentialAsync(user.Id, cancellationToken);
            if (credential is null)
            {
                credential = new UserCredential(Guid.NewGuid(), user.Id, now);
                transaction.AddCredential(credential);
            }

            var passwordHash = passwordHasher.Hash(user, request.InitialPassword);
            if (credential.Status == UserCredentialStatus.PendingActivation)
            {
                credential.Activate(passwordHash, now);
            }
            else if (credential.Status == UserCredentialStatus.Active &&
                     !passwordHasher.Verify(user, credential.PasswordHash!, request.InitialPassword))
            {
                credential.ChangePassword(passwordHash, now);
            }
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
