namespace BrothersCare.Application.Identity.Contracts;

public interface IPasswordRecoverySender
{
    // Expected delivery failures return false. Secrets must never be included in logs or exceptions.
    Task<bool> TrySendAsync(PasswordRecoveryDelivery delivery, CancellationToken cancellationToken);
}

public sealed class PasswordRecoveryDelivery(string email, string token, DateTimeOffset expiresAt)
{
    public string Email { get; } = email;
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public override string ToString() => "[REDACTED]";
}
