using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class AppointmentCompletionTests
{
    [Fact]
    public async Task CompleteMarksAnStartedAppointmentAsCompleted()
    {
        var context = CreateContext();
        var appointment = AddAppointment(context, context.Scenario.Clock.GetUtcNow().AddMinutes(-10));

        var result = await new CompleteAppointment(context.Scenario, context.Scenario.Store, context.Scenario.Clock)
            .ExecuteAsync(new CompleteAppointmentCommand(appointment.Id), CancellationToken.None);

        Assert.Equal(CompleteAppointmentStatus.Success, result.Status);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(context.Membership.Id, appointment.CompletedByMembershipId);
        Assert.Equal(context.Scenario.Clock.GetUtcNow(), appointment.CompletedAt);
    }

    [Fact]
    public async Task CompleteRejectsFutureAndFinalAppointments()
    {
        var context = CreateContext();
        var future = AddAppointment(context, context.Scenario.Clock.GetUtcNow().AddDays(1));
        var final = AddAppointment(context, context.Scenario.Clock.GetUtcNow().AddMinutes(-10));
        final.Cancel("Solicitação do paciente", context.Membership.Id, context.Scenario.Clock.GetUtcNow());
        var useCase = new CompleteAppointment(context.Scenario, context.Scenario.Store, context.Scenario.Clock);

        var futureResult = await useCase.ExecuteAsync(new CompleteAppointmentCommand(future.Id), CancellationToken.None);
        var finalResult = await useCase.ExecuteAsync(new CompleteAppointmentCommand(final.Id), CancellationToken.None);

        Assert.Equal(CompleteAppointmentStatus.NotStarted, futureResult.Status);
        Assert.Equal(CompleteAppointmentStatus.NotScheduled, finalResult.Status);
    }

    [Fact]
    public async Task NoShowMarksAnStartedAppointmentAndRequiresReason()
    {
        var context = CreateContext();
        var appointment = AddAppointment(context, context.Scenario.Clock.GetUtcNow().AddMinutes(-10));
        var useCase = new MarkAppointmentNoShow(context.Scenario, context.Scenario.Store, context.Scenario.Clock);

        var missingReason = await useCase.ExecuteAsync(new MarkAppointmentNoShowCommand(appointment.Id, " "), CancellationToken.None);
        var success = await useCase.ExecuteAsync(new MarkAppointmentNoShowCommand(appointment.Id, "  Paciente não compareceu  "), CancellationToken.None);

        Assert.Equal(MarkAppointmentNoShowStatus.InvalidInput, missingReason.Status);
        Assert.Equal(MarkAppointmentNoShowStatus.Success, success.Status);
        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Equal("Paciente não compareceu", appointment.NoShowReason);
        Assert.Equal(context.Membership.Id, appointment.NoShowByMembershipId);
    }

    private static AppointmentContext CreateContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var professional = new Professional(Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345", scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        var patient = new Patient(Guid.NewGuid(), tenant.Id, "Paciente da agenda", new DateOnly(1990, 1, 1), scenario.Clock.GetUtcNow());
        scenario.Store.Professionals.Add(professional);
        scenario.Store.Patients.Add(patient);
        scenario.SignIn(user, membership);
        return new(scenario, tenant, membership, unit, professional, patient);
    }

    private static Appointment AddAppointment(AppointmentContext context, DateTimeOffset startsAt)
    {
        var appointment = new Appointment(Guid.NewGuid(), context.Tenant.Id, context.Unit.Id, context.Patient.Id, context.Professional.Id, context.Membership.Id, startsAt, startsAt.AddHours(1), null, context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Appointments.Add(appointment);
        return appointment;
    }

    private sealed record AppointmentContext(IdentityScenario Scenario, Tenant Tenant, TenantMembership Membership, ClinicUnit Unit, Professional Professional, Patient Patient);
}
