using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.PasswordRecovery;

public sealed class RequestPasswordRecovery(
    IPasswordRecoveryStore store,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    AuthenticationSettings settings,
    IPasswordRecoverySender? sender)
{
    public async Task ExecuteAsync(string? email, CancellationToken cancellationToken)
    {
        if (sender is null || string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
            !MailAddress.TryCreate(email.Trim(), out var address) ||
            !string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var user = await transaction.FindUserAsync(email.Trim().ToUpperInvariant(), cancellationToken);
        if (user is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var credential = await transaction.FindCredentialAsync(user.Id, cancellationToken);
        credential?.UnlockIfExpired(now);
        if (credential?.Status != UserCredentialStatus.Active)
        {
            return;
        }

        var generated = tokenGenerator.Generate();
        var expiresAt = now.Add(settings.PasswordRecoveryTokenLifetime);
        await transaction.RevokeRecoveryTokensAsync(user.Id, now, null, cancellationToken);
        transaction.AddToken(new AccountActionToken(Guid.NewGuid(), user.Id,
            AccountActionTokenPurpose.PasswordRecovery, generated.SecretHash, now, expiresAt));
        await transaction.SaveAsync(cancellationToken);
        if (!await sender.TrySendAsync(new PasswordRecoveryDelivery(user.Email, generated.Secret, expiresAt), cancellationToken))
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
