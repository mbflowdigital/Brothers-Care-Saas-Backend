using BrothersCare.Application.Care;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Care;

public sealed class RegisterPatientTests
{
    [Fact]
    public async Task ClinicAdministratorRegistersPatientInActiveTenant()
    {
        var scenario = CreateScenario();

        var result = await Service(scenario).ExecuteAsync(
            "  Paciente de Teste  ",
            new DateOnly(1990, 5, 20),
            " Particular ",
            null,
            null,
            CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.Success, result.Status);
        Assert.Equal("Paciente de Teste", result.Patient!.FullName);
        Assert.Equal(PatientStatus.Active.ToString(), result.Patient.Status);
        Assert.Equal("Particular", result.Patient.HealthPlan);
        var patient = Assert.Single(scenario.Store.Patients);
        Assert.Equal(scenario.Principal!.ActiveTenantId, patient.TenantId);
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegistrationRejectsMissingName(string fullName)
    {
        var scenario = CreateScenario();

        var result = await Service(scenario).ExecuteAsync(
            fullName,
            new DateOnly(1990, 5, 20),
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.InvalidInput, result.Status);
        Assert.Empty(scenario.Store.Patients);
    }

    [Fact]
    public async Task ProfessionalCannotRegisterPatient()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        scenario.SignIn(user, membership);

        var result = await Service(scenario).ExecuteAsync(
            "Paciente de Teste",
            new DateOnly(1990, 5, 20),
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.Forbidden, result.Status);
        Assert.Empty(scenario.Store.Patients);
    }

    [Fact]
    public async Task RegistrationRejectsMissingBirthDate()
    {
        var scenario = CreateScenario();

        var result = await Service(scenario).ExecuteAsync(
            "Paciente de Teste",
            default,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.InvalidInput, result.Status);
        Assert.Empty(scenario.Store.Patients);
    }

    [Fact]
    public async Task RegistrationCreatesCareAssignmentForAvailableProfessionalAndUnit()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        var professional = new Professional(Guid.NewGuid(), tenant.Id, user.DisplayName, "Psicologia", "12345", scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membership.Id, user.DisplayName, "Psicologia", "CRP", "12345");
        scenario.Store.Professionals.Add(professional);

        var result = await Service(scenario).ExecuteAsync(
            "Paciente de Teste",
            new DateOnly(1990, 5, 20),
            "Saúde Prime",
            professional.Id,
            unit.Id,
            CancellationToken.None);

        Assert.Equal(PatientRegistrationStatus.Success, result.Status);
        var assignment = Assert.Single(scenario.Store.PatientCareAssignments);
        Assert.Equal(result.Patient!.Id, assignment.PatientId);
        Assert.Equal(professional.Id, assignment.ProfessionalId);
        Assert.Equal(unit.Id, assignment.ClinicUnitId);
    }

    private static RegisterPatient Service(IdentityScenario scenario) => new(scenario, scenario.Store, scenario.Clock);

    private static IdentityScenario CreateScenario()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        return scenario;
    }
}
