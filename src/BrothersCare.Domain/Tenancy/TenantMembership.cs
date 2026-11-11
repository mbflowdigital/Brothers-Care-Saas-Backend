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

    public void Activate()
    {
        if (Status is not (MembershipStatus.Active or MembershipStatus.Suspended))
        {
            throw new InvalidOperationException("Invited or revoked memberships require a valid invitation acceptance.");
        }

        Status = MembershipStatus.Active;
    }

    public void RenewInvitation(TenantMembershipRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (Status is not (MembershipStatus.Invited or MembershipStatus.Revoked))
        {
            throw new InvalidOperationException("Only an invited or revoked membership can receive a new invitation.");
        }

        Role = role;
        Status = MembershipStatus.Invited;
    }

    public static TenantMembership CreateInvitation(
        Guid id, Guid tenantId, Guid userId, TenantMembershipRole role, DateTimeOffset createdAt)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return new TenantMembership(id, tenantId, userId, role, createdAt)
        {
            Status = MembershipStatus.Invited,
        };
    }

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
