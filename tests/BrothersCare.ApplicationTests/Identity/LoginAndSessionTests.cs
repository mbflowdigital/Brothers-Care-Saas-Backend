using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Login;
using BrothersCare.Application.Identity.PasswordChange;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;

namespace BrothersCare.ApplicationTests.Identity;

public sealed class LoginAndSessionTests
{
    [Fact]
    public async Task LoginNormalizesEmailCreatesIndependentSessionsAndResetsFailures()
    {
        var scenario = new IdentityScenario();
        scenario.AddUser();
        var credential = Assert.Single(scenario.Store.Credentials);
        credential.RegisterFailedSignIn(scenario.Clock.GetUtcNow(), 5, TimeSpan.FromMinutes(15));
        var login = Login(scenario);
        var first = await login.ExecuteAsync(new LoginCommand(" USER@EXAMPLE.INVALID ", "TestPassword123"), CancellationToken.None);
        var second = await login.ExecuteAsync(new LoginCommand("user@example.invalid", "TestPassword123"), CancellationToken.None);

        Assert.Equal(LoginStatus.Succeeded, first.Status);
        Assert.Equal(LoginStatus.Succeeded, second.Status);
        Assert.Equal("USER@EXAMPLE.INVALID", scenario.Store.LastNormalizedEmail);
        Assert.Equal(0, credential.FailedSignInCount);
        Assert.Equal(2, scenario.Store.Sessions.Count);
        Assert.NotEqual(first.CookieValue, second.CookieValue);
        Assert.NotNull(first.Session);
        Assert.Null(first.Session.ActiveTenant);
        Assert.All(scenario.Store.Sessions, item =>
        {
            Assert.Null(item.ActiveTenantId);
            Assert.Equal(scenario.Clock.GetUtcNow().AddMinutes(30), item.IdleExpiresAt);
            Assert.Equal(scenario.Clock.GetUtcNow().AddHours(12), item.AbsoluteExpiresAt);
        });
        Assert.Equal(2, scenario.Events.Count(item => item == AuthenticationAuditEvent.LoginSucceeded));
    }

    [Fact]
    public async Task UnknownUserAndIncorrectPasswordReturnSameFailureWithoutIssuingSession()
    {
        var scenario = new IdentityScenario();
        scenario.AddUser();
        var login = Login(scenario);
        var known = await login.ExecuteAsync(new LoginCommand("user@example.invalid", "wrong-password"), CancellationToken.None);
        var unknown = await login.ExecuteAsync(new LoginCommand("unknown@example.invalid", "wrong-password"), CancellationToken.None);
        Assert.Equal(known.Status, unknown.Status);
        Assert.Null(known.Session);
        Assert.Null(unknown.Session);
        Assert.Null(known.CookieValue);
        Assert.Null(unknown.CookieValue);
        Assert.Equal(LoginStatus.InvalidCredentials, known.Status);
        Assert.Equal(1, scenario.Hasher.DummyChecks);
        Assert.Equal(1, Assert.Single(scenario.Store.Credentials).FailedSignInCount);
        Assert.Empty(scenario.Store.Sessions);
        Assert.DoesNotContain(AuthenticationAuditEvent.LoginSucceeded, scenario.Events);
    }

    [Fact]
    public async Task PendingCredentialCannotLoginOrAccumulateFailures()
    {
        var scenario = new IdentityScenario();
        scenario.AddUser(active: false);
        var result = await Login(scenario).ExecuteAsync(new LoginCommand("user@example.invalid", "TestPassword123"), CancellationToken.None);
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, Assert.Single(scenario.Store.Credentials).FailedSignInCount);
        Assert.Equal(0, scenario.Store.Commits);
        Assert.Equal(1, scenario.Hasher.DummyChecks);
    }

    [Theory]
    [InlineData("invalid-email", "password")]
    [InlineData("user@example.invalid", "")]
    [InlineData("", "password")]
    public async Task InvalidLoginInputDoesNotAccessStore(string email, string password)
    {
        var scenario = new IdentityScenario();
        var result = await Login(scenario).ExecuteAsync(new LoginCommand(email, password), CancellationToken.None);
        Assert.Equal(LoginStatus.InvalidInput, result.Status);
        Assert.Null(scenario.Store.LastNormalizedEmail);
        Assert.Empty(scenario.Store.Sessions);
    }

    [Fact]
    public async Task ConfiguredLockoutRevokesEverySessionAndExpiresAtDeadline()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        scenario.SignIn(user);
        scenario.SignIn(user);
        var settings = new AuthenticationSettings(maximumFailedAttempts: 2, lockoutMinutes: 3);
        var login = new LoginUser(scenario.Store, scenario.Hasher, scenario.Tokens, scenario.Clock, settings, scenario);
        await login.ExecuteAsync(new LoginCommand(user.Email, "wrong-password"), CancellationToken.None);
        Assert.All(scenario.Store.Sessions, session => Assert.Null(session.RevokedAt));
        await login.ExecuteAsync(new LoginCommand(user.Email, "wrong-password"), CancellationToken.None);
        Assert.All(scenario.Store.Sessions, session => Assert.Equal(SessionRevocationReason.SecurityIncident, session.RevocationReason));
        Assert.Contains(AuthenticationAuditEvent.AccountLocked, scenario.Events);
        Assert.Equal(LoginStatus.InvalidCredentials, (await login.ExecuteAsync(new LoginCommand(user.Email, "TestPassword123"), CancellationToken.None)).Status);
        scenario.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(LoginStatus.Succeeded, (await login.ExecuteAsync(new LoginCommand(user.Email, "TestPassword123"), CancellationToken.None)).Status);
        Assert.Equal(0, Assert.Single(scenario.Store.Credentials).FailedSignInCount);
    }

    [Fact]
    public async Task SessionBuildsPrincipalFromPersistedMembershipAndUnits()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, member, unit) = scenario.AddTenant(user);
        var cookie = scenario.SignIn(user, member);
        scenario.Clock.Advance(TimeSpan.FromMinutes(5));
        var authenticated = await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None);
        Assert.NotNull(authenticated);
        Assert.Equal(member.TenantId, authenticated.Principal.ActiveTenantId);
        Assert.Equal(member.Id, authenticated.Principal.TenantMembershipId);
        Assert.Equal([unit.Id], authenticated.Principal.ClinicUnitIds);
        Assert.Equal(scenario.Clock.GetUtcNow().AddMinutes(30), authenticated.Summary.ExpiresAt);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task InvalidCookiesCannotAuthenticate(string invalidity)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var cookie = scenario.SignIn(user);
        var session = Assert.Single(scenario.Store.Sessions);
        switch (invalidity)
        {
            case "malformed": cookie = "invalid-cookie"; break;
            case "unknown": cookie = $"{Guid.NewGuid():D}.{cookie[37..]}"; break;
            case "tampered": cookie = $"{session.Id:D}.{scenario.Tokens.Generate().Secret}"; break;
            case "expired": scenario.Clock.Advance(TimeSpan.FromMinutes(30)); break;
            case "revoked": session.Revoke(scenario.Clock.GetUtcNow(), SessionRevocationReason.Logout); break;
        }
        Assert.Null(await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None));
    }

    [Fact]
    public async Task LockedAccountRevokesSessionOnNextAuthentication()
    {
        var scenario = new IdentityScenario();
        var cookie = scenario.SignIn(scenario.AddUser());
        Assert.Single(scenario.Store.Credentials).RegisterFailedSignIn(scenario.Clock.GetUtcNow(), 1, TimeSpan.FromMinutes(15));
        Assert.Null(await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None));
        Assert.Equal(SessionRevocationReason.AccountDisabled, Assert.Single(scenario.Store.Sessions).RevocationReason);
    }

    [Fact]
    public async Task MembershipRevocationRemovesTenantRoleAndUnitAuthorization()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, member, _) = scenario.AddTenant(user);
        var cookie = scenario.SignIn(user, member);
        member.Deactivate();
        var session = await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None);
        Assert.Null(session);
    }

    [Fact]
    public async Task TenantSelectionRejectsOtherUsersTenantAndInvitedOrRevokedMembership()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var other = scenario.AddUser(email: "other@example.invalid");
        var (foreignTenant, _, _) = scenario.AddTenant(other);
        var (ownTenant, ownMembership, _) = scenario.AddTenant(user);
        scenario.SignIn(user);
        var select = new SelectActiveTenant(scenario, scenario.Store, scenario.Clock, scenario.Settings);
        Assert.Equal(TenantSelectionStatus.NotFound, (await select.ExecuteAsync(foreignTenant.Id, CancellationToken.None)).Status);
        Assert.Null(Assert.Single(scenario.Store.Sessions).ActiveTenantId);
        Assert.Equal(TenantSelectionStatus.Selected, (await select.ExecuteAsync(ownTenant.Id, CancellationToken.None)).Status);
        ownMembership.Deactivate();
        Assert.Equal(TenantSelectionStatus.NotFound, (await select.ExecuteAsync(ownTenant.Id, CancellationToken.None)).Status);
        ownMembership.RenewInvitation(ownMembership.Role);
        Assert.Equal(TenantSelectionStatus.NotFound, (await select.ExecuteAsync(ownTenant.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SuspendedMembershipCannotSelectTenantOrProvideAuthorizedUnits()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, _) = scenario.AddTenant(user);
        var cookie = scenario.SignIn(user);
        IdentityScenario.LoadSuspendedMembership(membership);
        Assert.Equal(TenantSelectionStatus.NotFound, (await new SelectActiveTenant(scenario, scenario.Store, scenario.Clock, scenario.Settings)
            .ExecuteAsync(tenant.Id, CancellationToken.None)).Status);
        var authenticated = await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None);
        Assert.NotNull(authenticated);
        Assert.Empty(authenticated.Summary.Memberships);
        Assert.Empty(authenticated.Principal.ClinicUnitIds);
    }

    [Fact]
    public async Task LogoutIsIdempotentAndStolenCookieCannotBeReused()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var cookie = scenario.SignIn(user);
        scenario.SignIn(user);
        var logout = new LogoutUser(scenario.Store, scenario.Tokens, scenario.Clock, scenario);
        await logout.ExecuteAsync(cookie, CancellationToken.None);
        await logout.ExecuteAsync(cookie, CancellationToken.None);
        await logout.ExecuteAsync(null, CancellationToken.None);
        Assert.Null(await Sessions(scenario).AuthenticateAsync(cookie, CancellationToken.None));
        Assert.Single(scenario.Store.Sessions, item => item.RevocationReason == SessionRevocationReason.Logout);
        Assert.Single(scenario.Store.Sessions, item => item.RevokedAt is null);
        Assert.Single(scenario.Events, item => item == AuthenticationAuditEvent.Logout);
    }

    [Fact]
    public async Task TamperedCookieCannotLogoutLegitimateSession()
    {
        var scenario = new IdentityScenario();
        var cookie = scenario.SignIn(scenario.AddUser());
        var altered = $"{cookie[..37]}{scenario.Tokens.Generate().Secret}";
        await new LogoutUser(scenario.Store, scenario.Tokens, scenario.Clock, scenario).ExecuteAsync(altered, CancellationToken.None);
        Assert.Null(Assert.Single(scenario.Store.Sessions).RevokedAt);
        Assert.Empty(scenario.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PasswordChangeRequiresCurrentPasswordAndRevokesAllUserSessionsOnly(bool correctPassword)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var otherUser = scenario.AddUser(email: "other@example.invalid");
        scenario.SignIn(otherUser);
        scenario.SignIn(user);
        scenario.SignIn(user);
        var change = new ChangePassword(scenario, scenario.Store, scenario.Hasher, scenario.Clock, scenario);
        var result = await change.ExecuteAsync(new ChangePasswordCommand(correctPassword ? "TestPassword123" : "wrong-password",
            "Replacement123", "Replacement123"), CancellationToken.None);
        Assert.Equal(correctPassword ? PasswordChangeStatus.Changed : PasswordChangeStatus.InvalidCurrentPassword, result);
        Assert.All(scenario.Store.Sessions.Where(item => item.UserId == user.Id), item =>
            Assert.Equal(correctPassword ? SessionRevocationReason.PasswordChanged : (SessionRevocationReason?)null, item.RevocationReason));
        Assert.Null(Assert.Single(scenario.Store.Sessions, item => item.UserId == otherUser.Id).RevokedAt);
    }

    private static LoginUser Login(IdentityScenario scenario) => new(scenario.Store, scenario.Hasher, scenario.Tokens,
        scenario.Clock, scenario.Settings, scenario);
    private static GetCurrentSession Sessions(IdentityScenario scenario) => new(scenario.Store, scenario.Tokens, scenario.Clock, scenario.Settings);
}
