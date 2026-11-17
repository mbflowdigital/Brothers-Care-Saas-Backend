using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class CancelAppointmentTests
{
    [Fact]
    public async Task CancelChangesStatusAndRecordsTheReasonAndActor()
    {
        var context = CreateContext();
        var appointment = AddAppointment(context);

        var result = await UseCase(context).ExecuteAsync(
            new CancelAppointmentCommand(appointment.Id, "  Solicitação do paciente  "),
            CancellationToken.None);

        Assert.Equal(CancelAppointmentStatus.Success, result.Status);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Solicitação do paciente", appointment.CancellationReason);
        Assert.Equal(context.Membership.Id, appointment.CancelledByMembershipId);
        Assert.Equal(context.Scenario.Clock.GetUtcNow(), appointment.CancelledAt);
        Assert.Equal(context.Membership.Id, appointment.UpdatedByMembershipId);
    }

    [Fact]
    public async Task CancelRejectsMissingReasonAndAppointmentsOutsideTheProfessionalScope()
    {
        var context = CreateContext();
        var appointment = AddAppointment(context);
        var otherProfessionalAppointment = new Appointment(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Unit.Id,
            context.Patient.Id,
            Guid.NewGuid(),
            context.Membership.Id,
            context.Scenario.Clock.GetUtcNow().AddDays(2),
            context.Scenario.Clock.GetUtcNow().AddDays(2).AddHours(1),
            null,
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Appointments.Add(otherProfessionalAppointment);

        var missingReason = await UseCase(context).ExecuteAsync(
            new CancelAppointmentCommand(appointment.Id, " "), CancellationToken.None);
        var outsideScope = await UseCase(context).ExecuteAsync(
            new CancelAppointmentCommand(otherProfessionalAppointment.Id, "Solicitação do paciente"),
            CancellationToken.None);

        Assert.Equal(CancelAppointmentStatus.InvalidInput, missingReason.Status);
        Assert.Equal(CancelAppointmentStatus.NotFound, outsideScope.Status);
    }

    [Fact]
    public async Task CancelRejectsAnAppointmentThatIsNoLongerScheduled()
    {
        var context = CreateContext();
        var appointment = AddAppointment(context);
        appointment.Cancel("Cancelamento inicial", context.Membership.Id, context.Scenario.Clock.GetUtcNow());

        var result = await UseCase(context).ExecuteAsync(
            new CancelAppointmentCommand(appointment.Id, "Tentativa posterior"),
            CancellationToken.None);

        Assert.Equal(CancelAppointmentStatus.NotScheduled, result.Status);
    }

    private static CancelAppointment UseCase(AppointmentContext context) =>
        new(context.Scenario, context.Scenario.Store, context.Scenario.Clock);

    private static Appointment AddAppointment(AppointmentContext context) =>
        AddAppointment(context, context.Scenario.Clock.GetUtcNow().AddDays(2));

    private static Appointment AddAppointment(AppointmentContext context, DateTimeOffset startsAt)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), context.Tenant.Id, context.Unit.Id, context.Patient.Id,
            context.Professional.Id, context.Membership.Id, startsAt, startsAt.AddHours(1), null,
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Appointments.Add(appointment);
        return appointment;
    }

    private static AppointmentContext CreateContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var professional = new Professional(
            Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345", scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        var patient = new Patient(
            Guid.NewGuid(), tenant.Id, "Paciente da agenda", new DateOnly(1990, 1, 1), scenario.Clock.GetUtcNow());
        scenario.Store.Professionals.Add(professional);
        scenario.Store.Patients.Add(patient);
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
