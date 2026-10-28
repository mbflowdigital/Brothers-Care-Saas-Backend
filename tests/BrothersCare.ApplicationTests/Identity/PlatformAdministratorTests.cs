using BrothersCare.Application.Identity;
using BrothersCare.Application.Identity.Login;
using BrothersCare.Application.Identity.Activation;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Application.Tenancy;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Identity;

public sealed class PlatformAdministratorTests
{
    [Fact]
    public async Task BootstrapIsIdempotentAndCreatesOnlyGlobalPendingIdentity()
    {
        var scenario = new IdentityScenario();
        var bootstrap = new PlatformAdministratorBootstrapper(scenario.Store, scenario.Clock, scenario);
        var request = new PlatformAdministratorBootstrapRequest(" GLOBAL@EXAMPLE.INVALID ", "Operador de teste");
        Assert.True((await bootstrap.EnsureSuperAdministratorAsync(request, CancellationToken.None)).Created);
        Assert.False((await bootstrap.EnsureSuperAdministratorAsync(request, CancellationToken.None)).Created);
        Assert.Single(scenario.Store.Users);
        Assert.Single(scenario.Store.Administrators);
        Assert.Equal(UserCredentialStatus.PendingActivation, Assert.Single(scenario.Store.Credentials).Status);
        Assert.Empty(scenario.Store.Memberships);
        Assert.Empty(scenario.Store.Tenants);
        Assert.Empty(scenario.Store.Sessions);
        Assert.Empty(scenario.Store.Tokens);
        Assert.Single(scenario.Events, item => item == Application.Identity.Contracts.AuthenticationAuditEvent.PlatformAdministratorCreated);
    }

    [Fact]
    public async Task BootstrapDoesNotReactivateInactiveAdministratorOrChangeExistingPassword()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var hash = Assert.Single(scenario.Store.Credentials).PasswordHash;
        var admin = new PlatformAdministrator(Guid.NewGuid(), user.Id, PlatformAdministratorRole.SuperAdministrator, scenario.Clock.GetUtcNow());
        admin.Deactivate();
        scenario.Store.Administrators.Add(admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformAdministratorBootstrapper(scenario.Store, scenario.Clock, scenario)
            .EnsureSuperAdministratorAsync(new PlatformAdministratorBootstrapRequest(user.Email, "Nome diferente"), CancellationToken.None));
        Assert.Equal(hash, Assert.Single(scenario.Store.Credentials).PasswordHash);
        Assert.Equal(PlatformAdministratorStatus.Inactive, admin.Status);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task InitialTokenReissueRevokesPreviousActivationTokenOnly()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false);
        scenario.Store.Administrators.Add(new PlatformAdministrator(Guid.NewGuid(), user.Id, PlatformAdministratorRole.SuperAdministrator, scenario.Clock.GetUtcNow()));
        scenario.AddToken(user, AccountActionTokenPurpose.PasswordRecovery);
        var issuer = new PlatformAdministratorActivationTokenIssuer(scenario.Store, scenario.Clock, scenario.Tokens);
        var request = new PlatformAdministratorActivationTokenIssueRequest(user.Email, TimeSpan.FromMinutes(30));
        var first = await issuer.IssueAsync(request, CancellationToken.None);
        var second = await issuer.IssueAsync(request, CancellationToken.None);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.AccountActivation && item.RevokedAt is not null);
        var current = Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.AccountActivation && item.RevokedAt is null);
        Assert.True(scenario.Tokens.Matches(second.Token, current.SecretHash));
        Assert.Equal(scenario.Clock.GetUtcNow().AddMinutes(30), second.ExpiresAt);
        Assert.Null(Assert.Single(scenario.Store.Tokens, item => item.Purpose == AccountActionTokenPurpose.PasswordRecovery).RevokedAt);
    }

    [Theory]
    [InlineData("active-credential")]
    [InlineData("inactive-admin")]
    [InlineData("not-admin")]
    [InlineData("unknown-user")]
    public async Task InitialTokenRequiresPendingAccountAndActiveSuperAdministrator(string invalidity)
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: invalidity == "active-credential");
        if (invalidity != "not-admin")
        {
            var admin = new PlatformAdministrator(Guid.NewGuid(), user.Id, PlatformAdministratorRole.SuperAdministrator, scenario.Clock.GetUtcNow());
            if (invalidity == "inactive-admin")
            {
                admin.Deactivate();
            }
            scenario.Store.Administrators.Add(admin);
        }
        var issuer = new PlatformAdministratorActivationTokenIssuer(scenario.Store, scenario.Clock, scenario.Tokens);
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(new PlatformAdministratorActivationTokenIssueRequest(
            invalidity == "unknown-user" ? "unknown@example.invalid" : user.Email, TimeSpan.FromMinutes(30)), CancellationToken.None));
        Assert.Empty(scenario.Store.Tokens);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task BootstrappedSuperAdministratorCanActivateAndLoginWithoutClinicalAccess()
    {
        var scenario = new IdentityScenario();
        await new PlatformAdministratorBootstrapper(scenario.Store, scenario.Clock, scenario).EnsureSuperAdministratorAsync(
            new PlatformAdministratorBootstrapRequest("global@example.invalid", "Operador de teste"), CancellationToken.None);
        var issued = await new PlatformAdministratorActivationTokenIssuer(scenario.Store, scenario.Clock, scenario.Tokens).IssueAsync(
            new PlatformAdministratorActivationTokenIssueRequest("global@example.invalid", TimeSpan.FromMinutes(30)), CancellationToken.None);
        Assert.Equal(AccountActivationResult.Activated, await new ActivateAccount(scenario.Store, scenario.Hasher, scenario.Tokens, scenario.Clock)
            .ActivateAsync(new AccountActivationRequest(issued.Token, "TestPassword123", "TestPassword123"), CancellationToken.None));
        var login = await new LoginUser(scenario.Store, scenario.Hasher, scenario.Tokens, scenario.Clock, scenario.Settings, scenario)
            .ExecuteAsync(new LoginCommand("global@example.invalid", "TestPassword123"), CancellationToken.None);
        Assert.Equal(LoginStatus.Succeeded, login.Status);
        Assert.NotNull(login.Session);
        Assert.Equal("SuperAdministrator", login.Session.PlatformAdministrator?.Role);
        Assert.Empty(login.Session.Memberships);
        Assert.Null(login.Session.ActiveTenant);
        var authenticated = await new GetCurrentSession(scenario.Store, scenario.Tokens, scenario.Clock, scenario.Settings)
            .AuthenticateAsync(login.CookieValue, CancellationToken.None);
        Assert.NotNull(authenticated);
        Assert.Null(authenticated.Principal.ActiveTenantId);
        Assert.Null(authenticated.Principal.TenantRole);
        Assert.Empty(authenticated.Principal.ClinicUnitIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompanyProvisioningInvitesAdministratorWithoutGivingActorClinicalAccess(bool existingUser)
    {
        var scenario = SuperAdministratorScenario();
        User? target = existingUser ? scenario.AddUser(email: "clinic@example.invalid") : null;
        var beforeCount = scenario.Store.Users.Count;
        var result = await Provision(scenario).ExecuteAsync(new CreateTenantWithAdministratorCommand(
            "Empresa de teste", "clinic@example.invalid", "Administrador convidado", "Primeira unidade"), CancellationToken.None);
        Assert.Equal(TenantProvisioningStatus.Created, result.Status);
        Assert.Equal(beforeCount + (existingUser ? 0 : 1), scenario.Store.Users.Count);
        var member = Assert.Single(scenario.Store.Memberships);
        Assert.NotEqual(scenario.Principal!.UserId, member.UserId);
        Assert.Equal(result.TenantId, member.TenantId);
        Assert.Equal(MembershipStatus.Invited, member.Status);
        Assert.Equal(TenantMembershipRole.ClinicAdministrator, member.Role);
        if (target is not null)
        {
            Assert.Equal(target.Id, member.UserId);
        }
        Assert.Equal(result.ClinicUnitId, Assert.Single(scenario.Store.UnitMemberships).ClinicUnitId);
        Assert.Equal(member.Id, Assert.Single(scenario.Store.Tokens).TenantMembershipId);
        Assert.Single(scenario.Invitations);
        Assert.Null(Assert.Single(scenario.Store.Sessions).ActiveTenantId);
    }

    [Theory]
    [InlineData("not-admin", TenantProvisioningStatus.Forbidden)]
    [InlineData("expired-session", TenantProvisioningStatus.Unauthenticated)]
    [InlineData("no-sender", TenantProvisioningStatus.DeliveryUnavailable)]
    public async Task CompanyProvisioningRequiresCurrentSessionActiveSuperadminAndDelivery(string invalidity, TenantProvisioningStatus expected)
    {
        var scenario = SuperAdministratorScenario();
        switch (invalidity)
        {
            case "not-admin": scenario.Store.Administrators.Clear(); break;
            case "expired-session": scenario.Clock.Advance(TimeSpan.FromMinutes(30)); break;
        }
        Assert.Equal(expected, (await Provision(scenario, invalidity != "no-sender").ExecuteAsync(
            new CreateTenantWithAdministratorCommand("Empresa de teste", "clinic@example.invalid", "Administrador convidado"), CancellationToken.None)).Status);
        Assert.Empty(scenario.Store.Tenants);
        Assert.Empty(scenario.Store.Memberships);
        Assert.Empty(scenario.Store.Tokens);
        Assert.Equal(0, scenario.Store.Commits);
    }

    private static IdentityScenario SuperAdministratorScenario()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        scenario.Store.Administrators.Add(new PlatformAdministrator(Guid.NewGuid(), user.Id, PlatformAdministratorRole.SuperAdministrator, scenario.Clock.GetUtcNow()));
        scenario.SignIn(user);
        return scenario;
    }

    private static CreateTenantWithAdministrator Provision(IdentityScenario scenario, bool senderAvailable = true) =>
        new(scenario.Store, scenario.Tokens, senderAvailable ? scenario : null, scenario.Clock, scenario, scenario);
}
