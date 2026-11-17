using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class GetSchedulingProfessionalsTests
{
    [Theory]
    [InlineData(TenantMembershipRole.ClinicAdministrator)]
    [InlineData(TenantMembershipRole.Receptionist)]
    public async Task ClinicManagerListsOnlyProfessionalsFromPermittedUnits(TenantMembershipRole actorRole)
    {
        var scenario = new IdentityScenario();
        var actor = scenario.AddUser();
        var (tenant, actorMembership, allowedUnit) = scenario.AddTenant(actor, actorRole);
        var hiddenUnit = new ClinicUnit(Guid.NewGuid(), tenant.Id, "Unidade restrita", scenario.Clock.GetUtcNow());
        scenario.Store.Units.Add(hiddenUnit);
        var visible = AddProfessional(scenario, tenant, allowedUnit, "Profissional visível");
        _ = AddProfessional(scenario, tenant, hiddenUnit, "Profissional restrito");
        scenario.SignIn(actor, actorMembership);

        var result = await new GetSchedulingProfessionals(scenario, scenario.Store, scenario.Clock)
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(SchedulingProfessionalsStatus.Success, result.Status);
        var professional = Assert.Single(result.Professionals!);
        Assert.Equal(visible.Id, professional.Id);
    }

    private static Professional AddProfessional(
        IdentityScenario scenario,
        Tenant tenant,
        ClinicUnit unit,
        string name)
    {
        var user = scenario.AddUser(email: $"{Guid.NewGuid():N}@example.invalid");
        var membership = new TenantMembership(Guid.NewGuid(), tenant.Id, user.Id,
            TenantMembershipRole.Professional, scenario.Clock.GetUtcNow());
        var professional = new Professional(Guid.NewGuid(), tenant.Id, name, "Fisioterapia",
            Guid.NewGuid().ToString("N"), scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membership.Id, name, "Fisioterapia", "CREFITO",
            Guid.NewGuid().ToString("N"));
        scenario.Store.Memberships.Add(membership);
        scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(Guid.NewGuid(), tenant.Id, unit.Id,
            membership.Id, scenario.Clock.GetUtcNow()));
        scenario.Store.Professionals.Add(professional);
        return professional;
    }
}
