using BrothersCare.Application.Identity.Activation;
using BrothersCare.Application.Identity.Invitations;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class TeamManagementTests
{
    [Fact]
    public async Task AdministratorCannotDeactivateSelfOrRemoveLastAdministrator()
    {
        var scenario = AdministratorScenario();
        var actor = Assert.Single(scenario.Store.Memberships);
        var service = new ManageTenantTeam(scenario, scenario.Store, scenario.Clock);

        Assert.Equal(TenantTeamMutationStatus.CannotDeactivateSelf,
            await service.DeactivateAsync(actor.Id, CancellationToken.None));
        Assert.Equal(TenantTeamMutationStatus.LastAdministrator,
            await service.UpdateAsync(actor.Id, TenantMembershipRole.Professional.ToString(),
                [scenario.Store.Units[0].Id], true, "Psicologia", "CRP", "06/123456", CancellationToken.None));
        Assert.Equal(MembershipStatus.Active, actor.Status);
        Assert.Equal(TenantMembershipRole.ClinicAdministrator, actor.Role);
    }

    [Fact]
    public async Task DeactivationRevokesAccessAndPreservesMembership()
    {
        var scenario = AdministratorScenario();
        var tenant = scenario.Store.Tenants[0];
        var targetUser = scenario.AddUser(email: "team@example.invalid");
        var target = new TenantMembership(Guid.NewGuid(), tenant.Id, targetUser.Id,
            TenantMembershipRole.Professional, scenario.Clock.GetUtcNow());
        scenario.Store.Memberships.Add(target);
        scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(Guid.NewGuid(), tenant.Id,
            scenario.Store.Units[0].Id, target.Id, scenario.Clock.GetUtcNow()));
        await scenario.Store.SetProfessionalProfileAsync(tenant.Id, target.Id, targetUser.DisplayName,
            true, "Fisioterapia", "CREFITO", "12345", scenario.Clock.GetUtcNow(), CancellationToken.None);

        var result = await new ManageTenantTeam(scenario, scenario.Store, scenario.Clock)
            .DeactivateAsync(target.Id, CancellationToken.None);

        Assert.Equal(TenantTeamMutationStatus.Success, result);
        Assert.Equal(MembershipStatus.Revoked, target.Status);
        Assert.Contains(target, scenario.Store.Memberships);
        Assert.Equal(ProfessionalStatus.Inactive, Assert.Single(scenario.Store.Professionals).Status);
    }

    [Fact]
    public async Task ValidInvitationExposesOnlyExpectedContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser(active: false, email: "invited@example.invalid");
        var now = scenario.Clock.GetUtcNow();
        var tenant = new Tenant(Guid.NewGuid(), "Clínica Serena", now);
        var unit = new ClinicUnit(Guid.NewGuid(), tenant.Id, "Unidade Centro", now);
        var membership = TenantMembership.CreateInvitation(Guid.NewGuid(), tenant.Id, user.Id,
            TenantMembershipRole.Receptionist, now);
        scenario.Store.Tenants.Add(tenant); scenario.Store.Units.Add(unit); scenario.Store.Memberships.Add(membership);
        scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(Guid.NewGuid(), tenant.Id, unit.Id, membership.Id, now));
        var presentedToken = scenario.AddToken(user, AccountActionTokenPurpose.InvitationAcceptance, membership.Id);

        var context = await new GetInvitationContext(scenario.Store, scenario.Tokens, scenario.Clock)
            .ExecuteAsync(presentedToken, CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal("Clínica Serena", context.TenantName);
        Assert.Equal("Receptionist", context.Role);
        Assert.Equal("Unidade Centro", Assert.Single(context.Units).Name);
    }

    private static IdentityScenario AdministratorScenario()
    {
        var scenario = new IdentityScenario();
        var actor = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(actor);
        scenario.SignIn(actor, membership);
        return scenario;
    }
}
