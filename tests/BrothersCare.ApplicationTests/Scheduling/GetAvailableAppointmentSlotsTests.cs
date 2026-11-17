using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class GetAvailableAppointmentSlotsTests
{
    [Fact]
    public async Task SlotsRespectWeeklyWindowsBlocksAndProfessionalAndPatientConflicts()
    {
        var context = CreateAuthorizedContext();
        var date = NextWeek(context);
        AddWindow(context, date, new(9, 0), new(11, 0));
        context.Scenario.Store.Appointments.Add(CreateAppointment(
            context, Guid.NewGuid(), context.Professional.Id, At(context, date, 9, 30), At(context, date, 10, 0)));
        context.Scenario.Store.Appointments.Add(CreateAppointment(
            context, context.Patient.Id, Guid.NewGuid(), At(context, date, 10, 30), At(context, date, 11, 0)));
        context.Scenario.Store.AvailabilityBlocks.Add(new ProfessionalAvailabilityBlock(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Professional.Id,
            At(context, date, 10, 0),
            At(context, date, 10, 30),
            AvailabilityBlockKind.Blocked,
            null,
            context.Scenario.Clock.GetUtcNow()));

        var result = await Service(context).ExecuteAsync(
            context.Unit.Id, date, 30, context.Patient.Id, null, CancellationToken.None);

        Assert.Equal(AvailableAppointmentSlotsStatus.Success, result.Status);
        var slot = Assert.Single(result.Slots!);
        Assert.Equal(At(context, date, 9, 0), slot.StartsAt);
        Assert.Equal(At(context, date, 9, 30), slot.EndsAt);
    }

    [Fact]
    public async Task ReschedulingDoesNotConflictWithTheAppointmentBeingMoved()
    {
        var context = CreateAuthorizedContext();
        var date = NextWeek(context);
        AddWindow(context, date, new(9, 0), new(10, 0));
        var appointment = CreateAppointment(
            context, context.Patient.Id, context.Professional.Id,
            At(context, date, 9, 0), At(context, date, 10, 0));
        context.Scenario.Store.Appointments.Add(appointment);

        var result = await Service(context).ExecuteAsync(
            context.Unit.Id, date, 60, null, appointment.Id, CancellationToken.None);

        Assert.Equal(AvailableAppointmentSlotsStatus.Success, result.Status);
        var slot = Assert.Single(result.Slots!);
        Assert.Equal(appointment.StartsAt, slot.StartsAt);
        Assert.Equal(appointment.EndsAt, slot.EndsAt);
    }

    [Fact]
    public async Task UnconfiguredWeeklyScheduleKeepsTheExistingDefaultSlotRange()
    {
        var context = CreateAuthorizedContext();
        var date = NextWeek(context);

        var result = await Service(context).ExecuteAsync(
            context.Unit.Id, date, 60, context.Patient.Id, null, CancellationToken.None);

        Assert.Equal(AvailableAppointmentSlotsStatus.Success, result.Status);
        Assert.Equal(28, result.Slots!.Count);
        Assert.Equal(At(context, date, 7, 0), result.Slots[0].StartsAt);
        Assert.Equal(At(context, date, 20, 30), result.Slots[^1].StartsAt);
    }

    private static GetAvailableAppointmentSlots Service(AppointmentContext context) =>
        new(context.Scenario, context.Scenario.Store, context.Scenario.Clock);

    private static DateOnly NextWeek(AppointmentContext context)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(context.Unit.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            context.Scenario.Clock.GetUtcNow(), timeZone).DateTime);
        return today.AddDays(7);
    }

    private static DateTimeOffset At(AppointmentContext context, DateOnly date, int hour, int minute)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(context.Unit.TimeZoneId);
        var local = date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static void AddWindow(
        AppointmentContext context,
        DateOnly date,
        TimeOnly startsAt,
        TimeOnly endsAt) =>
        context.Scenario.Store.AvailabilityWindows.Add(new ProfessionalAvailabilityWindow(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Professional.Id,
            context.Unit.Id,
            date.DayOfWeek,
            startsAt,
            endsAt,
            context.Scenario.Clock.GetUtcNow()));

    private static Appointment CreateAppointment(
        AppointmentContext context,
        Guid patientId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(Guid.NewGuid(), context.Tenant.Id, context.Unit.Id, patientId, professionalId,
            context.Membership.Id, startsAt, endsAt, null, context.Scenario.Clock.GetUtcNow());

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
