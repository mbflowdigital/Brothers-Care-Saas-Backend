using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.DomainTests.Identity;

public sealed class AuthenticationStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CredentialLocksAtConfiguredThresholdAndUnlocksOnlyAtDeadline()
    {
        var credential = Credential();
        credential.RegisterFailedSignIn(Now, 2, TimeSpan.FromMinutes(3));
        Assert.Equal(UserCredentialStatus.Active, credential.Status);
        credential.RegisterFailedSignIn(Now, 2, TimeSpan.FromMinutes(3));
        Assert.Equal(UserCredentialStatus.Locked, credential.Status);
        credential.UnlockIfExpired(Now.AddMinutes(3).AddTicks(-1));
        Assert.Equal(UserCredentialStatus.Locked, credential.Status);
        credential.UnlockIfExpired(Now.AddMinutes(3));
        Assert.Equal(UserCredentialStatus.Active, credential.Status);
        Assert.Equal(0, credential.FailedSignInCount);
        Assert.Null(credential.LockoutEndsAt);
    }

    [Fact]
    public void SuccessfulSignInResetsConsecutiveFailures()
    {
        var credential = Credential();
        credential.RegisterFailedSignIn(Now, 2, TimeSpan.FromMinutes(15));
        credential.RegisterSuccessfulSignIn(Now);
        credential.RegisterFailedSignIn(Now, 2, TimeSpan.FromMinutes(15));
        Assert.Equal(UserCredentialStatus.Active, credential.Status);
        Assert.Equal(1, credential.FailedSignInCount);
    }

    [Fact]
    public void ActivationCannotOverwriteAnExistingPassword()
    {
        var credential = Credential();
        Assert.Throws<InvalidOperationException>(() => credential.Activate("different-test-hash", Now));
        Assert.Equal("test-only-hash", credential.PasswordHash);
        Assert.Equal(Now, credential.PasswordChangedAt);
    }

    [Fact]
    public void PendingAndLockedCredentialsCannotChangePasswordOrSignIn()
    {
        var pending = new UserCredential(Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => pending.ChangePassword("test-only-hash", Now));
        Assert.Throws<InvalidOperationException>(() => pending.RegisterSuccessfulSignIn(Now));
        var locked = Credential();
        locked.RegisterFailedSignIn(Now, 1, TimeSpan.FromMinutes(15));
        Assert.Throws<InvalidOperationException>(() => locked.ChangePassword("test-only-hash", Now));
        Assert.Throws<InvalidOperationException>(() => locked.RegisterFailedSignIn(Now, 1, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void SessionRenewsIdleTimeWithoutExtendingAbsoluteExpiry()
    {
        var session = Session();
        session.RegisterActivity(Now.AddMinutes(20), TimeSpan.FromHours(24));
        Assert.Equal(Now.AddHours(12), session.IdleExpiresAt);
        Assert.Equal(Now.AddHours(12), session.AbsoluteExpiresAt);
        Assert.False(session.IsValidAt(Now.AddHours(12)));
    }

    [Fact]
    public void LastSeenChangesAtMostOnceEveryFiveMinutes()
    {
        var session = Session();
        session.RegisterActivity(Now.AddMinutes(4), TimeSpan.FromMinutes(30));
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddMinutes(34), session.IdleExpiresAt);
        session.RegisterActivity(Now.AddMinutes(5), TimeSpan.FromMinutes(30));
        Assert.Equal(Now.AddMinutes(5), session.LastSeenAt);
        session.RegisterActivity(Now.AddMinutes(9), TimeSpan.FromMinutes(30));
        Assert.Equal(Now.AddMinutes(5), session.LastSeenAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredOrRevokedSessionCannotBeRenewed(bool revoke)
    {
        var session = Session();
        var deadline = Now.AddMinutes(30);
        if (revoke)
        {
            session.Revoke(Now, SessionRevocationReason.Logout);
        }
        Assert.False(session.IsValidAt(deadline));
        Assert.Throws<InvalidOperationException>(() => session.RegisterActivity(deadline, TimeSpan.FromMinutes(30)));
        Assert.False(session.IsValidAt(Now.AddMinutes(-1)));
    }

    [Fact]
    public void RepeatedRevocationPreservesOriginalReasonAndTime()
    {
        var session = Session();
        session.Revoke(Now, SessionRevocationReason.Logout);
        session.Revoke(Now.AddMinutes(1), SessionRevocationReason.PasswordChanged);
        Assert.Equal(Now, session.RevokedAt);
        Assert.Equal(SessionRevocationReason.Logout, session.RevocationReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionCannotSelectInvitedRevokedOrForeignMembership(bool revoke)
    {
        var session = Session();
        var membership = TenantMembership.CreateInvitation(Guid.NewGuid(), Guid.NewGuid(), session.UserId,
            TenantMembershipRole.Professional, Now);
        if (revoke)
        {
            membership.Deactivate();
        }
        Assert.Throws<InvalidOperationException>(() => session.SelectActiveTenant(membership, Now));
        var foreignUserMembership = new TenantMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            TenantMembershipRole.Professional, Now);
        Assert.Throws<InvalidOperationException>(() => session.SelectActiveTenant(foreignUserMembership, Now));
        Assert.Null(session.ActiveTenantId);
    }

    [Fact]
    public void MembershipRequiresAcceptanceAndRevokedMembershipRequiresNewInvitation()
    {
        var membership = TenantMembership.CreateInvitation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            TenantMembershipRole.Professional, Now);
        Assert.Throws<InvalidOperationException>(membership.Activate);
        membership.AcceptInvitation();
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Throws<InvalidOperationException>(() => membership.RenewInvitation(TenantMembershipRole.ClinicAdministrator));
        membership.Deactivate();
        Assert.Throws<InvalidOperationException>(membership.Activate);
        Assert.Throws<InvalidOperationException>(membership.AcceptInvitation);
        membership.RenewInvitation(TenantMembershipRole.Receptionist);
        Assert.Equal(MembershipStatus.Invited, membership.Status);
        membership.AcceptInvitation();
        Assert.Equal(TenantMembershipRole.Receptionist, membership.Role);
    }

    [Fact]
    public void ConsumedTokenCannotBeUsedAgainOrRevoked()
    {
        var token = Token();
        token.Consume(Now);
        Assert.False(token.IsUsableAt(Now));
        Assert.Throws<InvalidOperationException>(() => token.Consume(Now));
        Assert.Throws<InvalidOperationException>(() => token.Revoke(Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredAndRevokedTokensCannotBeConsumed(bool revoke)
    {
        var token = Token();
        if (revoke)
        {
            token.Revoke(Now);
        }
        var when = revoke ? Now : Now.AddMinutes(30);
        Assert.False(token.IsUsableAt(when));
        Assert.Throws<InvalidOperationException>(() => token.Consume(when));
    }

    [Fact]
    public void OnlyInvitationTokensRequireMembershipReference()
    {
        Assert.Throws<ArgumentException>(() => new AccountActionToken(Guid.NewGuid(), Guid.NewGuid(),
            AccountActionTokenPurpose.InvitationAcceptance, new byte[32], Now, Now.AddMinutes(30)));
        Assert.Throws<ArgumentException>(() => new AccountActionToken(Guid.NewGuid(), Guid.NewGuid(),
            AccountActionTokenPurpose.PasswordRecovery, new byte[32], Now, Now.AddMinutes(30), Guid.NewGuid()));
    }

    [Fact]
    public void InvalidExpirationWindowsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new UserSession(Guid.NewGuid(), Guid.NewGuid(), new byte[32],
            Now, Now.AddMinutes(30), Now.AddMinutes(29)));
        Assert.Throws<ArgumentException>(() => new AccountActionToken(Guid.NewGuid(), Guid.NewGuid(),
            AccountActionTokenPurpose.AccountActivation, new byte[32], Now, Now));
    }

    private static UserCredential Credential()
    {
        var credential = new UserCredential(Guid.NewGuid(), Guid.NewGuid(), Now);
        credential.Activate("test-only-hash", Now);
        return credential;
    }

    private static UserSession Session() => new(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Now,
        Now.AddMinutes(30), Now.AddHours(12));

    private static AccountActionToken Token() => new(Guid.NewGuid(), Guid.NewGuid(),
        AccountActionTokenPurpose.AccountActivation, new byte[32], Now, Now.AddMinutes(30));
}
