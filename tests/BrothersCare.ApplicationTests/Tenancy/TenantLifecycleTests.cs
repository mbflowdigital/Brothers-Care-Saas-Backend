using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class TenantLifecycleTests
{
    [Fact]
    public async Task SuspendingTenantRevokesOnlySessionsUsingThatTenant()
    {
        var scenario = CreateScenario();
        var tenant = scenario.Store.Tenants[0];
        var administrator = scenario.Store.Users[0];
        var external = scenario.AddTenant(administrator);
        scenario.SignIn(administrator, external.Membership);
        scenario.Principal = new CurrentPrincipal(administrator.Id, scenario.Store.Sessions[0].Id, tenant.Id,
            "SuperAdministrator", scenario.Store.Memberships[0].Id, TenantMembershipRole.ClinicAdministrator.ToString(), []);

        var result = await Service(scenario).ExecuteAsync(tenant.Id, TenantLifecycleAction.Suspend, CancellationToken.None);

        Assert.Equal(TenantLifecycleStatus.Changed, result.Status);
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(SessionRevocationReason.TenantUnavailable, scenario.Store.Sessions[0].RevocationReason);
        Assert.Null(scenario.Store.Sessions[1].RevokedAt);
    }

    [Fact]
    public async Task SuspendedTenantCanBeReactivatedWithoutRevivingSessions()
    {
        var scenario = CreateScenario();
        var tenant = scenario.Store.Tenants[0];
        tenant.Suspend();

        var result = await Service(scenario).ExecuteAsync(tenant.Id, TenantLifecycleAction.Reactivate, CancellationToken.None);

        Assert.Equal(TenantLifecycleStatus.Changed, result.Status);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(scenario.Store.Sessions[0].RevokedAt);
    }

    [Fact]
    public async Task NonPlatformAdministratorCannotChangeTenantLifecycle()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);

        var result = await Service(scenario).ExecuteAsync(tenant.Id, TenantLifecycleAction.Suspend, CancellationToken.None);

        Assert.Equal(TenantLifecycleStatus.Forbidden, result.Status);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    private static ChangeTenantLifecycle Service(IdentityScenario scenario) =>
        new(scenario, scenario.Store, scenario.Clock);

    private static IdentityScenario CreateScenario()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        scenario.Store.Administrators.Add(new PlatformAdministrator(
            Guid.NewGuid(), user.Id, PlatformAdministratorRole.SuperAdministrator, scenario.Clock.GetUtcNow()));
        var (_, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        return scenario;
    }
}
