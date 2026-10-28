using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Activation;
using BrothersCare.Application.Identity.PasswordRecovery;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Identity;

public sealed class ActivationAndRecoveryTests
{
    [Fact]
    public async Task ActivationSetsPasswordConsumesTokenAndRevokesPreviousSessions()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false);
        scenario.SignIn(user);
        var token = scenario.AddToken(user, AccountActionTokenPurpose.AccountActivation);
        var activation = Activate(scenario);
        var request = new AccountActivationRequest(token, "NewPassword123", "NewPassword123");
        Assert.Equal(AccountActivationResult.Activated, await activation.ActivateAsync(request, CancellationToken.None));
        var credential = Assert.Single(scenario.Store.Credentials);
        Assert.Equal(UserCredentialStatus.Active, credential.Status);
        Assert.True(scenario.Hasher.Verify(user, credential.PasswordHash!, "NewPassword123"));
        Assert.NotNull(Assert.Single(scenario.Store.Tokens).ConsumedAt);
        Assert.Equal(SessionRevocationReason.PasswordChanged, Assert.Single(scenario.Store.Sessions).RevocationReason);
        Assert.Equal(1, scenario.Store.Commits);
        Assert.Equal(AccountActivationResult.InvalidToken, await activation.ActivateAsync(request, CancellationToken.None));
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("consumed")]
    [InlineData("purpose")]
    [InlineData("unknown")]
    public async Task ActivationRejectsUnusableTokensWithoutActivatingCredential(string invalidity)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false);
        var token = scenario.AddToken(user, invalidity == "purpose" ? AccountActionTokenPurpose.PasswordRecovery : AccountActionTokenPurpose.AccountActivation);
        var stored = Assert.Single(scenario.Store.Tokens);
        switch (invalidity)
        {
            case "expired": scenario.Clock.Advance(TimeSpan.FromMinutes(30)); break;
            case "revoked": stored.Revoke(scenario.Clock.GetUtcNow()); break;
            case "consumed": stored.Consume(scenario.Clock.GetUtcNow()); break;
            case "unknown": token = scenario.Tokens.Generate().Secret; break;
        }
        Assert.Equal(AccountActivationResult.InvalidToken, await Activate(scenario).ActivateAsync(
            new AccountActivationRequest(token, "NewPassword123", "NewPassword123"), CancellationToken.None));
        Assert.Equal(UserCredentialStatus.PendingActivation, Assert.Single(scenario.Store.Credentials).Status);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Theory]
    [InlineData("short1", "short1")]
    [InlineData("OnlyLettersHere", "OnlyLettersHere")]
    [InlineData("123456789012", "123456789012")]
    [InlineData("NewPassword123", "Different1234")]
    public async Task ActivationRejectsWeakPasswordsOrDivergentConfirmation(string password, string confirmation)
    {
        var scenario = new IdentityScenario();
        var token = scenario.AddToken(scenario.AddUser(active: false), AccountActionTokenPurpose.AccountActivation);
        Assert.Equal(AccountActivationResult.InvalidPassword, await Activate(scenario).ActivateAsync(
            new AccountActivationRequest(token, password, confirmation), CancellationToken.None));
        Assert.Null(Assert.Single(scenario.Store.Tokens).ConsumedAt);
        Assert.Null(Assert.Single(scenario.Store.Credentials).PasswordHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvitationAcceptanceActivatesOnlyAssociatedMembershipAndPreservesExistingPassword(bool existingAccount)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: existingAccount);
        var (_, member, _) = scenario.AddTenant(user);
        member.Deactivate();
        member.RenewInvitation(member.Role);
        var (_, otherMembership, _) = scenario.AddTenant(user);
        otherMembership.Deactivate();
        otherMembership.RenewInvitation(otherMembership.Role);
        var oldHash = Assert.Single(scenario.Store.Credentials).PasswordHash;
        var token = scenario.AddToken(user, AccountActionTokenPurpose.InvitationAcceptance, member.Id);
        var password = existingAccount ? "TestPassword123" : "NewPassword123";
        Assert.Equal(AccountActivationResult.Activated, await Activate(scenario).ActivateAsync(
            new AccountActivationRequest(token, password, password), CancellationToken.None));
        Assert.Equal(MembershipStatus.Active, member.Status);
        Assert.Equal(MembershipStatus.Invited, otherMembership.Status);
        if (existingAccount)
        {
            Assert.Equal(oldHash, Assert.Single(scenario.Store.Credentials).PasswordHash);
        }
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Fact]
    public async Task InvitationCannotResetExistingAccountPasswordOrActivateAnotherUsersMembership()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, member, _) = scenario.AddTenant(user);
        member.Deactivate();
        member.RenewInvitation(member.Role);
        var token = scenario.AddToken(user, AccountActionTokenPurpose.InvitationAcceptance, member.Id);
        var original = Assert.Single(scenario.Store.Credentials).PasswordHash;
        var activation = Activate(scenario);
        Assert.Equal(AccountActivationResult.InvalidToken, await activation.ActivateAsync(
            new AccountActivationRequest(token, "AttackerPass123", "AttackerPass123"), CancellationToken.None));
        Assert.Equal(original, Assert.Single(scenario.Store.Credentials).PasswordHash);
        var other = scenario.AddUser(email: "other@example.invalid");
        var mismatchedToken = scenario.AddToken(other, AccountActionTokenPurpose.InvitationAcceptance, member.Id);
        Assert.Equal(AccountActivationResult.InvalidToken, await activation.ActivateAsync(
            new AccountActivationRequest(mismatchedToken, "TestPassword123", "TestPassword123"), CancellationToken.None));
        Assert.Equal(MembershipStatus.Invited, member.Status);
        Assert.All(scenario.Store.Tokens, item => Assert.Null(item.ConsumedAt));
    }

    [Fact]
    public async Task SuspendedTenantCannotBeActivatedByInvitation()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false);
        var (tenant, membership, _) = scenario.AddTenant(user);
        membership.Deactivate();
        membership.RenewInvitation(membership.Role);
        tenant.Suspend();
        var token = scenario.AddToken(user, AccountActionTokenPurpose.InvitationAcceptance, membership.Id);
        Assert.Equal(AccountActivationResult.InvalidToken, await Activate(scenario).ActivateAsync(
            new AccountActivationRequest(token, "NewPassword123", "NewPassword123"), CancellationToken.None));
        Assert.Equal(MembershipStatus.Invited, membership.Status);
    }

    [Fact]
    public async Task RecoveryRequestReplacesOnlyPreviousRecoveryTokensAndDoesNotDiscloseUnknownUser()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        scenario.AddToken(user, AccountActionTokenPurpose.PasswordRecovery);
        scenario.AddToken(user, AccountActionTokenPurpose.AccountActivation);
        var request = new RequestPasswordRecovery(scenario.Store, scenario.Tokens, scenario.Clock, scenario.Settings, scenario);
        await request.ExecuteAsync(" USER@EXAMPLE.INVALID ", CancellationToken.None);
        var sent = Assert.Single(scenario.Recoveries);
        Assert.Equal(user.Email, sent.Email);
        Assert.Equal(scenario.Clock.GetUtcNow().AddMinutes(30), sent.ExpiresAt);
        Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.PasswordRecovery && item.RevokedAt is not null);
        Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.PasswordRecovery && item.RevokedAt is null);
        Assert.Null(Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.AccountActivation).RevokedAt);
        await request.ExecuteAsync("unknown@example.invalid", CancellationToken.None);
        Assert.Single(scenario.Recoveries);
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Fact]
    public async Task RecoveryWithoutSenderOrWithPendingAccountDoesNotCreateToken()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false);
        await new RequestPasswordRecovery(scenario.Store, scenario.Tokens, scenario.Clock, scenario.Settings, scenario)
            .ExecuteAsync(user.Email, CancellationToken.None);
        await new RequestPasswordRecovery(scenario.Store, scenario.Tokens, scenario.Clock, scenario.Settings, null)
            .ExecuteAsync(user.Email, CancellationToken.None);
        Assert.Empty(scenario.Store.Tokens);
        Assert.Empty(scenario.Recoveries);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task RecoveryConsumesTokenChangesPasswordAndRevokesEveryUserSessionAndOtherRecoveryToken()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var other = scenario.AddUser(email: "other@example.invalid");
        scenario.SignIn(user);
        scenario.SignIn(user);
        scenario.SignIn(other);
        var token = scenario.AddToken(user, AccountActionTokenPurpose.PasswordRecovery);
        scenario.AddToken(user, AccountActionTokenPurpose.PasswordRecovery);
        var confirm = Recovery(scenario);
        var command = new ConfirmPasswordRecoveryCommand(token, "Replacement123", "Replacement123");
        Assert.True(await confirm.ExecuteAsync(command, CancellationToken.None));
        var credential = Assert.Single(scenario.Store.Credentials, item => item.UserId == user.Id);
        Assert.True(scenario.Hasher.Verify(user, credential.PasswordHash!, "Replacement123"));
        Assert.All(scenario.Store.Sessions.Where(item => item.UserId == user.Id), item => Assert.Equal(SessionRevocationReason.PasswordChanged, item.RevocationReason));
        Assert.Null(Assert.Single(scenario.Store.Sessions, item => item.UserId == other.Id).RevokedAt);
        Assert.Single(scenario.Store.Tokens, item => item.ConsumedAt is not null);
        Assert.Single(scenario.Store.Tokens, item => item.RevokedAt is not null);
        Assert.False(await confirm.ExecuteAsync(command, CancellationToken.None));
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("consumed")]
    [InlineData("purpose")]
    [InlineData("changed-password")]
    [InlineData("confirmation")]
    public async Task InvalidRecoveryCannotChangePasswordOrRevokeSessions(string invalidity)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        scenario.SignIn(user);
        var token = scenario.AddToken(user, invalidity == "purpose" ? AccountActionTokenPurpose.AccountActivation : AccountActionTokenPurpose.PasswordRecovery);
        var stored = Assert.Single(scenario.Store.Tokens);
        var credential = Assert.Single(scenario.Store.Credentials);
        switch (invalidity)
        {
            case "expired": scenario.Clock.Advance(TimeSpan.FromMinutes(30)); break;
            case "revoked": stored.Revoke(scenario.Clock.GetUtcNow()); break;
            case "consumed": stored.Consume(scenario.Clock.GetUtcNow()); break;
            case "changed-password":
                scenario.Clock.Advance(TimeSpan.FromSeconds(1));
                credential.ChangePassword(scenario.Hasher.Hash(user, "LaterPassword123"), scenario.Clock.GetUtcNow());
                break;
        }
        var original = credential.PasswordHash;
        Assert.False(await Recovery(scenario).ExecuteAsync(new ConfirmPasswordRecoveryCommand(token,
            "Replacement123", invalidity == "confirmation" ? "Mismatch1234" : "Replacement123"), CancellationToken.None));
        Assert.Equal(original, credential.PasswordHash);
        Assert.Null(Assert.Single(scenario.Store.Sessions).RevokedAt);
        Assert.Equal(0, scenario.Store.Commits);
    }

    private static ActivateAccount Activate(IdentityScenario scenario) => new(scenario.Store, scenario.Hasher, scenario.Tokens, scenario.Clock);
    private static ConfirmPasswordRecovery Recovery(IdentityScenario scenario) => new(scenario.Store, scenario.Hasher, scenario.Tokens, scenario.Clock, scenario);
}
