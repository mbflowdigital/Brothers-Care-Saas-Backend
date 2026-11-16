using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class CreateAppointmentTests
{
    [Fact]
    public async Task ProfessionalCreatesAppointmentForAssignedPatientAtLinkedUnit()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);

        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt, startsAt.AddHours(1), "  Retorno  "),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.Success, result.Status);
        var appointment = Assert.Single(context.Scenario.Store.Appointments);
        Assert.Equal(context.Professional.Id, appointment.ProfessionalId);
        Assert.Equal(context.Membership.Id, appointment.CreatedByMembershipId);
        Assert.Equal("Retorno", appointment.Notes);
        Assert.Equal(1, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task MissingSessionIsForbiddenWithoutOpeningScopeFromPayload()
    {
        var scenario = new IdentityScenario();
        var now = scenario.Clock.GetUtcNow().AddDays(1);

        var result = await Service(scenario).ExecuteAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(1), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.Forbidden, result.Status);
        Assert.Empty(scenario.Store.Appointments);
    }

    [Fact]
    public async Task ActiveProfessionalProfileIsRequired()
    {
        var context = CreateAuthorizedContext();
        context.Professional.Inactivate();

        var result = await ExecuteValidAsync(context);

        Assert.Equal(CreateAppointmentStatus.ProfessionalNotFound, result.Status);
    }

    [Fact]
    public async Task UnitMustBeActiveAndLinkedToProfessional()
    {
        var context = CreateAuthorizedContext();
        var otherUnit = new ClinicUnit(Guid.NewGuid(), context.Tenant.Id, "Outra unidade",
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Units.Add(otherUnit);

        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, otherUnit.Id, startsAt, startsAt.AddHours(1), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.ClinicUnitNotAvailable, result.Status);
    }

    [Fact]
    public async Task PatientMustBeActiveAndAssignedToProfessional()
    {
        var context = CreateAuthorizedContext();
        context.Patient.Inactivate();

        var result = await ExecuteValidAsync(context);

        Assert.Equal(CreateAppointmentStatus.PatientNotAssigned, result.Status);
    }

    [Fact]
    public async Task AppointmentCannotStartInPast()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddMinutes(-1);

        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt, startsAt.AddHours(1), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.StartsInPast, result.Status);
    }

    [Fact]
    public async Task ProfessionalOverlapIsRejected()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        context.Scenario.Store.Appointments.Add(CreateExistingAppointment(
            context, Guid.NewGuid(), context.Professional.Id, startsAt, startsAt.AddHours(1)));

        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt.AddMinutes(30), startsAt.AddHours(2), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.ProfessionalConflict, result.Status);
    }

    [Fact]
    public async Task PatientOverlapWithAnotherProfessionalIsRejected()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        context.Scenario.Store.Appointments.Add(CreateExistingAppointment(
            context, context.Patient.Id, Guid.NewGuid(), startsAt, startsAt.AddHours(1)));

        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt.AddMinutes(30), startsAt.AddHours(2), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.PatientConflict, result.Status);
    }

    [Fact]
    public async Task AdjacentAppointmentsDoNotConflict()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        context.Scenario.Store.Appointments.Add(CreateExistingAppointment(
            context, context.Patient.Id, context.Professional.Id, startsAt, startsAt.AddHours(1)));

        var result = await Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt.AddHours(1), startsAt.AddHours(2), null),
            CancellationToken.None);

        Assert.Equal(CreateAppointmentStatus.Success, result.Status);
    }

    private static Task<CreateAppointmentResult> ExecuteValidAsync(AppointmentContext context)
    {
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        return Service(context.Scenario).ExecuteAsync(
            new(context.Patient.Id, context.Unit.Id, startsAt, startsAt.AddHours(1), null),
            CancellationToken.None);
    }

    private static CreateAppointment Service(IdentityScenario scenario) =>
        new(scenario, scenario.Store, scenario.Clock);

    private static AppointmentContext CreateAuthorizedContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var professional = new Professional(Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345",
            scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        var patient = new Patient(Guid.NewGuid(), tenant.Id, "Paciente teste", new DateOnly(1990, 1, 1),
            scenario.Clock.GetUtcNow());
        var assignment = new PatientCareAssignment(Guid.NewGuid(), tenant.Id, patient.Id, professional.Id, unit.Id,
            DateOnly.FromDateTime(scenario.Clock.GetUtcNow().UtcDateTime));
        scenario.Store.Professionals.Add(professional);
        scenario.Store.Patients.Add(patient);
        scenario.Store.PatientCareAssignments.Add(assignment);
        scenario.SignIn(user, membership);
        return new(scenario, tenant, membership, unit, professional, patient);
    }

    private static Appointment CreateExistingAppointment(
        AppointmentContext context,
        Guid patientId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(Guid.NewGuid(), context.Tenant.Id, context.Unit.Id, patientId, professionalId, context.Membership.Id,
            startsAt, endsAt, null, context.Scenario.Clock.GetUtcNow());

    private sealed record AppointmentContext(
        IdentityScenario Scenario,
        Tenant Tenant,
        TenantMembership Membership,
        ClinicUnit Unit,
        Professional Professional,
        Patient Patient);
}
