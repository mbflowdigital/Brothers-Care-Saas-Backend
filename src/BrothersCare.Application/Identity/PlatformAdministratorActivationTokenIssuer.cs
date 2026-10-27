using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity;

public sealed class PlatformAdministratorActivationTokenIssuer(
    IPlatformAdministratorStore store,
    TimeProvider timeProvider,
    ITokenGenerator tokenGenerator) : IPlatformAdministratorActivationTokenIssuer
{
    public async Task<PlatformAdministratorActivationTokenIssueResult> IssueAsync(
        PlatformAdministratorActivationTokenIssueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The activation token lifetime must be between zero and 24 hours.");
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        await using var transaction = await store.BeginAsync(cancellationToken);
        var user = await transaction.FindUserAsync(normalizedEmail, cancellationToken)
            ?? throw new InvalidOperationException("The requested platform administrator was not found.");

        var administrator = await transaction.FindAdministratorAsync(user.Id, cancellationToken);
        if (administrator is null || administrator.Role != PlatformAdministratorRole.SuperAdministrator ||
            administrator.Status != PlatformAdministratorStatus.Active)
        {
            throw new InvalidOperationException("The requested user is not an active super administrator.");
        }

        var credential = await transaction.FindCredentialAsync(user.Id, cancellationToken);
        if (credential is not null && credential.Status != UserCredentialStatus.PendingActivation)
        {
            throw new InvalidOperationException("Only a pending credential can receive an initial activation token.");
        }

        var now = timeProvider.GetUtcNow();
        if (credential is null)
        {
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }
        await transaction.RevokeActivationTokensAsync(user.Id, now, cancellationToken);

        var generatedToken = tokenGenerator.Generate();
        transaction.AddToken(new AccountActionToken(
            Guid.NewGuid(),
            user.Id,
            AccountActionTokenPurpose.AccountActivation,
            generatedToken.SecretHash,
            now,
            now.Add(request.Lifetime)));

        await transaction.CommitAsync(cancellationToken);
        return new PlatformAdministratorActivationTokenIssueResult(generatedToken.Secret, now.Add(request.Lifetime));
    }
}
