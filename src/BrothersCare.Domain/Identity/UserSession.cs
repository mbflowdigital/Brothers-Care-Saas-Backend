namespace BrothersCare.Domain.Identity;

public sealed class UserSession
{
    private UserSession() { }

    public UserSession(Guid id, Guid userId, byte[] secretHash, DateTimeOffset createdAt,
        DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Session and user ids are required.");
        }

        ArgumentNullException.ThrowIfNull(secretHash);
        if (secretHash.Length != 32 || idleExpiresAt <= createdAt || absoluteExpiresAt < idleExpiresAt)
        {
            throw new ArgumentException("A SHA-256 hash and valid expiration window are required.");
        }

        Id = id;
        UserId = userId;
        SecretHash = (byte[])secretHash.Clone();
        CreatedAt = LastSeenAt = createdAt;
        IdleExpiresAt = idleExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public Guid? ActiveTenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset IdleExpiresAt { get; private set; }
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public SessionRevocationReason? RevocationReason { get; private set; }
}

public enum SessionRevocationReason
{
    Logout = 1,
    PasswordChanged = 2,
    AccountDisabled = 3,
    SecurityIncident = 4,
    Expired = 5,
}
