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

    public void UnlockIfExpired(DateTimeOffset now)
    {
        if (Status == UserCredentialStatus.Locked && LockoutEndsAt <= now)
        {
            Status = UserCredentialStatus.Active;
            FailedSignInCount = 0;
            LockoutEndsAt = null;
            UpdatedAt = now;
        }
    }

    public void RegisterFailedSignIn(DateTimeOffset now, int maximumAttempts, TimeSpan lockoutDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockoutDuration, TimeSpan.Zero);

        if (Status != UserCredentialStatus.Active)
        {
            throw new InvalidOperationException("Only an active credential can register a sign-in failure.");
        }

        FailedSignInCount++;
        UpdatedAt = now;
        if (FailedSignInCount >= maximumAttempts)
        {
            Status = UserCredentialStatus.Locked;
            LockoutEndsAt = now.Add(lockoutDuration);
        }
    }

    public void RegisterSuccessfulSignIn(DateTimeOffset now)
    {
        if (Status != UserCredentialStatus.Active)
        {
            throw new InvalidOperationException("Only an active credential can sign in.");
        }

        FailedSignInCount = 0;
        LockoutEndsAt = null;
        UpdatedAt = now;
    }

    public void Activate(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (Status is not UserCredentialStatus.PendingActivation)
        {
            throw new InvalidOperationException("Only a pending credential can be activated. Existing credentials require a dedicated password change or recovery flow.");
        }

        PasswordHash = passwordHash;
        Status = UserCredentialStatus.Active;
        FailedSignInCount = 0;
        LockoutEndsAt = null;
        PasswordChangedAt = now;
        UpdatedAt = now;
    }

    public void ChangePassword(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (Status != UserCredentialStatus.Active)
        {
            throw new InvalidOperationException("Only an active credential can change its password.");
        }

        PasswordHash = passwordHash;
        PasswordChangedAt = UpdatedAt = now;
        FailedSignInCount = 0;
        LockoutEndsAt = null;
    }
}

public enum UserCredentialStatus
{
    PendingActivation = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
}
