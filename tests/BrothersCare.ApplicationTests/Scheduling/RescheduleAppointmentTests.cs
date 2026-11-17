using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class RescheduleAppointmentTests
{
    [Fact]
    public async Task RescheduleUpdatesTheScheduledAppointmentAndExcludesItFromConflictValidation()
    {
        var context = CreateContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);
        var appointment = AddAppointment(context, startsAt);
        var newStartsAt = startsAt.AddHours(2);

        var result = await UseCase(context).ExecuteAsync(
            new RescheduleAppointmentCommand(appointment.Id, context.Unit.Id, newStartsAt, newStartsAt.AddHours(1)),
            CancellationToken.None);

        Assert.Equal(RescheduleAppointmentStatus.Success, result.Status);
        Assert.Equal(newStartsAt, appointment.StartsAt);
        Assert.Equal(newStartsAt.AddHours(1), appointment.EndsAt);
        Assert.Equal(context.Membership.Id, appointment.UpdatedByMembershipId);
        Assert.Equal(context.Scenario.Clock.GetUtcNow(), appointment.UpdatedAt);
    }

    [Fact]
    public async Task RescheduleRejectsConflictingOrPastTime()
    {
        var context = CreateContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);
        var appointment = AddAppointment(context, startsAt);
        AddAppointment(context, startsAt.AddHours(3));

        var conflict = await UseCase(context).ExecuteAsync(
            new RescheduleAppointmentCommand(
                appointment.Id, context.Unit.Id, startsAt.AddHours(3), startsAt.AddHours(4)),
            CancellationToken.None);
        var past = await UseCase(context).ExecuteAsync(
            new RescheduleAppointmentCommand(
                appointment.Id, context.Unit.Id, startsAt.AddDays(-3), startsAt.AddDays(-3).AddHours(1)),
            CancellationToken.None);

        Assert.Equal(RescheduleAppointmentStatus.ProfessionalConflict, conflict.Status);
        Assert.Equal(RescheduleAppointmentStatus.StartsInPast, past.Status);
    }

    [Fact]
    public async Task RescheduleRejectsFinalStateAndOtherProfessionalAppointment()
    {
        var context = CreateContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);
        var completed = AddAppointment(context, startsAt);
        completed.MarkCompleted(context.Membership.Id, startsAt);
        var otherProfessionalAppointment = new Appointment(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Unit.Id,
            context.Patient.Id,
            Guid.NewGuid(),
            context.Membership.Id,
            startsAt.AddDays(1),
            startsAt.AddDays(1).AddHours(1),
            null,
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Appointments.Add(otherProfessionalAppointment);

        var finalState = await UseCase(context).ExecuteAsync(
            new RescheduleAppointmentCommand(completed.Id, context.Unit.Id, startsAt.AddHours(2), startsAt.AddHours(3)),
            CancellationToken.None);
        var outsideScope = await UseCase(context).ExecuteAsync(
            new RescheduleAppointmentCommand(
                otherProfessionalAppointment.Id,
                context.Unit.Id,
                startsAt.AddDays(1).AddHours(2),
                startsAt.AddDays(1).AddHours(3)),
            CancellationToken.None);

        Assert.Equal(RescheduleAppointmentStatus.NotScheduled, finalState.Status);
        Assert.Equal(RescheduleAppointmentStatus.NotFound, outsideScope.Status);
    }

    private static RescheduleAppointment UseCase(AppointmentContext context) =>
        new(context.Scenario, context.Scenario.Store, context.Scenario.Clock);

    private static Appointment AddAppointment(AppointmentContext context, DateTimeOffset startsAt)
    {
        var appointment = new Appointment(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Unit.Id,
            context.Patient.Id,
            context.Professional.Id,
            context.Membership.Id,
            startsAt,
            startsAt.AddHours(1),
            null,
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
