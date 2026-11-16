using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class GetSchedulingOptionsTests
{
    [Fact]
    public async Task ReturnsOnlyActivePatientsAndUnitsAvailableToProfessional()
    {
        var context = CreateAuthorizedContext();
        var unassignedPatient = new Patient(
            Guid.NewGuid(), context.Tenant.Id, "Paciente não vinculado", new DateOnly(1990, 1, 1),
            context.Scenario.Clock.GetUtcNow());
        var inactivePatient = new Patient(
            Guid.NewGuid(), context.Tenant.Id, "Paciente inativo", new DateOnly(1990, 1, 1),
            context.Scenario.Clock.GetUtcNow());
        inactivePatient.Inactivate();
        context.Scenario.Store.Patients.Add(unassignedPatient);
        context.Scenario.Store.Patients.Add(inactivePatient);
        context.Scenario.Store.PatientCareAssignments.Add(new PatientCareAssignment(
            Guid.NewGuid(), context.Tenant.Id, inactivePatient.Id, context.Professional.Id, context.Unit.Id,
            DateOnly.FromDateTime(context.Scenario.Clock.GetUtcNow().UtcDateTime)));

        var inactiveUnit = new ClinicUnit(
            Guid.NewGuid(), context.Tenant.Id, "Unidade inativa", context.Scenario.Clock.GetUtcNow());
        inactiveUnit.Deactivate();
        context.Scenario.Store.Units.Add(inactiveUnit);
        context.Scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(
            Guid.NewGuid(), context.Tenant.Id, inactiveUnit.Id, context.Membership.Id,
            context.Scenario.Clock.GetUtcNow()));

        var result = await Service(context.Scenario).ExecuteAsync(CancellationToken.None);

        Assert.Equal(SchedulingOptionsStatus.Success, result.Status);
        Assert.NotNull(result.Options);
        Assert.Collection(result.Options.Patients,
            patient => Assert.Equal(context.Patient.Id, patient.Id));
        Assert.Collection(result.Options.ClinicUnits,
            unit =>
            {
                Assert.Equal(context.Unit.Id, unit.Id);
                Assert.Equal(context.Unit.TimeZoneId, unit.TimeZoneId);
            });
        Assert.Equal(45, result.Options.DefaultAppointmentDurationMinutes);
    }

    [Fact]
    public async Task RequiresAnActiveProfessionalProfile()
    {
        var context = CreateAuthorizedContext();
        context.Professional.Inactivate();

        var result = await Service(context.Scenario).ExecuteAsync(CancellationToken.None);

        Assert.Equal(SchedulingOptionsStatus.ProfessionalNotFound, result.Status);
        Assert.Null(result.Options);
    }

    private static GetSchedulingOptions Service(IdentityScenario scenario) =>
        new(scenario, scenario.Store, scenario.Clock);

    private static AppointmentContext CreateAuthorizedContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var professional = new Professional(
            Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345",
            scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(
            membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345",
            defaultAppointmentDurationMinutes: 45);
        var patient = new Patient(
            Guid.NewGuid(), tenant.Id, "Paciente vinculado", new DateOnly(1990, 1, 1),
            scenario.Clock.GetUtcNow());
        scenario.Store.Professionals.Add(professional);
        scenario.Store.Patients.Add(patient);
        scenario.Store.PatientCareAssignments.Add(new PatientCareAssignment(
            Guid.NewGuid(), tenant.Id, patient.Id, professional.Id, unit.Id,
            DateOnly.FromDateTime(scenario.Clock.GetUtcNow().UtcDateTime)));
        scenario.SignIn(user, membership);
        return new(scenario, tenant, membership, unit, professional, patient);
    }

    private sealed record AppointmentContext(
        IdentityScenario Scenario,
        Tenant Tenant,
        TenantMembership Membership,
        ClinicUnit Unit,
        Professional Professional,
        Patient Patient);
}
