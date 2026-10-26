namespace BrothersCare.Domain.Identity;

public sealed class AccountActionToken
{
    private AccountActionToken() { }

    public AccountActionToken(Guid id, Guid userId, AccountActionTokenPurpose purpose, byte[] secretHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, Guid? tenantMembershipId = null)
    {
        if (id == Guid.Empty || userId == Guid.Empty || !Enum.IsDefined(purpose))
        {
            throw new ArgumentException("Token, user and purpose are required.");
        }

        ArgumentNullException.ThrowIfNull(secretHash);
        if (secretHash.Length != 32 || expiresAt <= createdAt)
        {
            throw new ArgumentException("A SHA-256 hash and valid expiration window are required.");
        }

        if (tenantMembershipId == Guid.Empty ||
            (purpose == AccountActionTokenPurpose.InvitationAcceptance) != tenantMembershipId.HasValue)
        {
            throw new ArgumentException("Only invitation tokens must reference a membership.");
        }

        Id = id;
        UserId = userId;
        Purpose = purpose;
        SecretHash = (byte[])secretHash.Clone();
        TenantMembershipId = tenantMembershipId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? TenantMembershipId { get; private set; }
    public AccountActionTokenPurpose Purpose { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
}

public enum AccountActionTokenPurpose
{
    AccountActivation = 1,
    PasswordRecovery = 2,
    InvitationAcceptance = 3,
}
