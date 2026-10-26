namespace BrothersCare.Domain.Identity;

public sealed class UserCredential
{
    private UserCredential() { }

    public UserCredential(Guid id, Guid userId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Credential and user ids are required.");
        }

        Id = id;
        UserId = userId;
        Status = UserCredentialStatus.PendingActivation;
        CreatedAt = UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? PasswordHash { get; private set; }
    public UserCredentialStatus Status { get; private set; }
    public int FailedSignInCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

public enum UserCredentialStatus
{
    PendingActivation = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
}
