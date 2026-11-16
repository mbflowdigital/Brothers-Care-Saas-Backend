using BrothersCare.Application.Care;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Care;

public sealed class ListPatientsTests
{
    [Fact]
    public async Task ProfessionalListsOnlyPatientsWithActiveAssignmentToOwnProfile()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        scenario.SignIn(user, membership);
        var professional = AddProfessional(scenario, tenant.Id, membership.Id, user.DisplayName);
        var assigned = AddPatient(scenario, tenant.Id, "Paciente vinculado");
        var another = AddPatient(scenario, tenant.Id, "Paciente de outro profissional");
        scenario.Store.PatientCareAssignments.Add(new PatientCareAssignment(
            Guid.NewGuid(), tenant.Id, assigned.Id, professional.Id, unit.Id, new DateOnly(2026, 1, 1)));

        var result = await Service(scenario).ExecuteAsync(null, 1, 10, CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.Success, result.Status);
        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(assigned.Id, item.Id);
        Assert.DoesNotContain(result.Page.Items, candidate => candidate.Id == another.Id);
    }

    [Fact]
    public async Task ClinicAdministratorListsAllTenantPatients()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        AddPatient(scenario, tenant.Id, "Paciente um");
        AddPatient(scenario, tenant.Id, "Paciente dois");

        var result = await Service(scenario).ExecuteAsync(null, 1, 10, CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.Success, result.Status);
        Assert.Equal(2, result.Page!.TotalItems);
    }

    private static ListPatients Service(IdentityScenario scenario) => new(scenario, scenario.Store, scenario.Clock);

    private static Patient AddPatient(IdentityScenario scenario, Guid tenantId, string name)
    {
        var patient = new Patient(Guid.NewGuid(), tenantId, name, new DateOnly(1990, 1, 1), scenario.Clock.GetUtcNow());
        scenario.Store.Patients.Add(patient);
        return patient;
    }

    private static Professional AddProfessional(
        IdentityScenario scenario,
        Guid tenantId,
        Guid membershipId,
        string name)
    {
        var professional = new Professional(
            Guid.NewGuid(), tenantId, name, "Fisioterapia", "12345", scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membershipId, name, "Fisioterapia", "CREFITO", "12345");
        scenario.Store.Professionals.Add(professional);
        return professional;
    }
}
