namespace BrothersCare.Application.Identity.Contracts;

public interface IInvitationSender
{
    Task SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken);
}

public sealed class InvitationDelivery(
    string email, string displayName, string tenantName, string token, DateTimeOffset expiresAt)
{
    public string Email { get; } = email;
    public string DisplayName { get; } = displayName;
    public string TenantName { get; } = tenantName;
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public override string ToString() => "[REDACTED]";
}
