namespace BrothersCare.Domain.Identity;

public sealed class PlatformAdministrator
{
    private PlatformAdministrator()
    {
    }

    public PlatformAdministrator(
        Guid id,
        Guid userId,
        PlatformAdministratorRole role,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Platform administrator and user ids are required.");
        }

        Id = id;
        UserId = userId;
        Role = role;
        Status = PlatformAdministratorStatus.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public PlatformAdministratorRole Role { get; private set; }

    public PlatformAdministratorStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Deactivate() => Status = PlatformAdministratorStatus.Inactive;

    public void Activate() => Status = PlatformAdministratorStatus.Active;
}

public enum PlatformAdministratorRole
{
    SuperAdministrator = 1,
}

public enum PlatformAdministratorStatus
{
    Active = 1,
    Inactive = 2,
}
