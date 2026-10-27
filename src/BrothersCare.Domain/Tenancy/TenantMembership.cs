namespace BrothersCare.Domain.Tenancy;

public sealed class TenantMembership
{
    private TenantMembership()
    {
    }

    public TenantMembership(Guid id, Guid tenantId, Guid userId, TenantMembershipRole role, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Membership, tenant and user ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public TenantMembershipRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void ChangeRole(TenantMembershipRole role) => Role = role;

    public void Deactivate() => Status = MembershipStatus.Revoked;

    public void Activate() => Status = MembershipStatus.Active;

    public void AcceptInvitation()
    {
        if (Status is not MembershipStatus.Invited)
        {
            throw new InvalidOperationException("Only an invited membership can be accepted.");
        }

        Status = MembershipStatus.Active;
    }
}

public enum TenantMembershipRole
{
    ClinicAdministrator = 1,
    Professional = 2,
    Receptionist = 3,
}

public enum MembershipStatus
{
    Active = 1,
    Invited = 3,
    Suspended = 4,
    Revoked = 5,
}
