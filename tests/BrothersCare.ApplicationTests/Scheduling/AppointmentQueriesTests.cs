using BrothersCare.Application.Scheduling;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class AppointmentQueriesTests
{
    [Fact]
    public async Task ListUsesInclusiveStartRangeAndOnlyReturnsCurrentProfessionalAppointments()
    {
        var context = CreateAuthorizedContext();
        var from = context.Scenario.Clock.GetUtcNow().AddDays(1);
        var to = from.AddDays(3);
        var atStart = AddAppointment(context, context.Tenant.Id, context.Professional.Id, from);
        AddAppointment(context, context.Tenant.Id, context.Professional.Id, to);
        AddAppointment(context, context.Tenant.Id, context.Professional.Id, to.AddTicks(1));
        AddAppointment(context, context.Tenant.Id, Guid.NewGuid(), from.AddHours(1));
        AddOtherTenantAppointment(context, from.AddHours(2));

        var result = await List(context.Scenario).ExecuteAsync(
            from, to, null, null, null, CancellationToken.None);

        Assert.Equal(AppointmentQueryStatus.Success, result.Status);
        var appointments = Assert.IsType<AppointmentScheduleItem[]>(result.Appointments);
        Assert.Equal(2, appointments.Length);
        Assert.Equal(atStart.Id, appointments[0].Id);
        Assert.Equal(context.Professional.FullName, appointments[0].ProfessionalName);
        Assert.Equal(context.Patient.FullName, appointments[0].PatientName);
        Assert.Equal(context.Unit.Name, appointments[0].ClinicUnitName);
        Assert.Equal(60m, appointments[0].DurationMinutes);
    }

    [Fact]
    public async Task ListCanFilterByStatusAndRejectPeriodsOverNinetyThreeDays()
    {
        var context = CreateAuthorizedContext();
        var from = context.Scenario.Clock.GetUtcNow().AddDays(1);
        var completed = AddAppointment(context, context.Tenant.Id, context.Professional.Id, from);
        completed.MarkCompleted(context.Membership.Id, completed.EndsAt);

        var filtered = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(2), "Completed", null, null, CancellationToken.None);
        var invalidRange = await List(context.Scenario).ExecuteAsync(
            from, from.Add(ListAppointments.MaximumPeriod).AddTicks(1), null, null, null, CancellationToken.None);
        var invalidStatus = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(2), "999", null, null, CancellationToken.None);

        Assert.Equal(AppointmentQueryStatus.Success, filtered.Status);
        Assert.Equal("Completed", Assert.Single(filtered.Appointments!).Status);
        Assert.Equal(AppointmentQueryStatus.InvalidInput, invalidRange.Status);
        Assert.Equal(AppointmentQueryStatus.InvalidInput, invalidStatus.Status);
    }

    [Fact]
    public async Task GetReturnsNotFoundForAppointmentsOutsideProfessionalScope()
    {
        var context = CreateAuthorizedContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(1);
        var ownAppointment = AddAppointment(context, context.Tenant.Id, context.Professional.Id, startsAt);
        var otherProfessionalAppointment = AddAppointment(context, context.Tenant.Id, Guid.NewGuid(), startsAt);
        var otherTenantAppointment = AddOtherTenantAppointment(context, startsAt);

        var own = await Get(context.Scenario).ExecuteAsync(ownAppointment.Id, CancellationToken.None);
        var otherProfessional = await Get(context.Scenario)
            .ExecuteAsync(otherProfessionalAppointment.Id, CancellationToken.None);
        var otherTenant = await Get(context.Scenario)
            .ExecuteAsync(otherTenantAppointment.Id, CancellationToken.None);

        Assert.Equal(AppointmentQueryStatus.Success, own.Status);
        Assert.Equal(AppointmentQueryStatus.NotFound, otherProfessional.Status);
        Assert.Equal(AppointmentQueryStatus.NotFound, otherTenant.Status);
    }

    [Fact]
    public async Task ClinicAdministratorCanFilterTenantAgendaButProfessionalCannotChangeScope()
    {
        var context = CreateAuthorizedContext();
        var now = context.Scenario.Clock.GetUtcNow();
        var secondUnit = new ClinicUnit(Guid.NewGuid(), context.Tenant.Id, "Unidade secundária", now);
        context.Scenario.Store.Units.Add(secondUnit);

        var secondUser = context.Scenario.AddUser(email: "second-professional@example.invalid");
        var secondMembership = new TenantMembership(
            Guid.NewGuid(), context.Tenant.Id, secondUser.Id, TenantMembershipRole.Professional, now);
        var secondProfessional = new Professional(
            Guid.NewGuid(), context.Tenant.Id, secondUser.DisplayName, "Fonoaudiologia", "67890", now);
        secondProfessional.ConfigureForMembership(
            secondMembership.Id, secondUser.DisplayName, "Fonoaudiologia", "CREFONO", "67890");
        context.Scenario.Store.Memberships.Add(secondMembership);
        context.Scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(
            Guid.NewGuid(), context.Tenant.Id, secondUnit.Id, secondMembership.Id, now));
        context.Scenario.Store.Professionals.Add(secondProfessional);

        var from = now.AddDays(1);
        var ownAppointment = AddAppointment(context, context.Tenant.Id, context.Professional.Id, from);
        var secondAppointment = AddAppointment(
            context, context.Tenant.Id, secondProfessional.Id, from.AddHours(2), secondUnit.Id);
        AddOtherTenantAppointment(context, from.AddHours(4));

        var administrator = context.Scenario.AddUser(email: "clinic-admin@example.invalid");
        var administratorMembership = new TenantMembership(
            Guid.NewGuid(), context.Tenant.Id, administrator.Id, TenantMembershipRole.ClinicAdministrator, now);
        context.Scenario.Store.Memberships.Add(administratorMembership);
        context.Scenario.SignIn(administrator, administratorMembership);

        var all = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(1), null, null, null, CancellationToken.None);
        var byProfessional = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(1), null, secondProfessional.Id, null, CancellationToken.None);
        var byUnit = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(1), null, null, secondUnit.Id, CancellationToken.None);
        var administratorDetail = await Get(context.Scenario)
            .ExecuteAsync(secondAppointment.Id, CancellationToken.None);

        Assert.Equal(AppointmentQueryStatus.Success, all.Status);
        Assert.Equal(
            new[] { ownAppointment.Id, secondAppointment.Id }.OrderBy(id => id),
            all.Appointments!.Select(item => item.Id).OrderBy(id => id));
        Assert.Equal(secondUser.DisplayName,
            Assert.Single(byProfessional.Appointments!).ProfessionalName);
        Assert.Equal(secondAppointment.Id, Assert.Single(byUnit.Appointments!).Id);
        Assert.Equal(AppointmentQueryStatus.Success, administratorDetail.Status);
        Assert.Equal(secondAppointment.Id, administratorDetail.Appointment!.Id);

        context.Scenario.SignIn(context.User, context.Membership);
        var attemptedScopeChange = await List(context.Scenario).ExecuteAsync(
            from, from.AddDays(1), null, secondProfessional.Id, null, CancellationToken.None);
        Assert.Equal(AppointmentQueryStatus.Forbidden, attemptedScopeChange.Status);
    }

    private static ListAppointments List(IdentityScenario scenario) =>
        new(scenario, scenario.Store, scenario.Clock);

    private static GetAppointment Get(IdentityScenario scenario) =>
        new(scenario, scenario.Store, scenario.Clock);

    private static Appointment AddAppointment(
        AppointmentContext context,
        Guid tenantId,
        Guid professionalId,
        DateTimeOffset startsAt,
        Guid? clinicUnitId = null)
    {
        var appointment = new Appointment(
            Guid.NewGuid(),
            tenantId,
            clinicUnitId ?? context.Unit.Id,
            context.Patient.Id,
            professionalId,
            context.Membership.Id,
            startsAt,
            startsAt.AddHours(1),
            null,
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Appointments.Add(appointment);
        return appointment;
    }

    private static Appointment AddOtherTenantAppointment(AppointmentContext context, DateTimeOffset startsAt)
    {
        var tenantId = Guid.NewGuid();
        var patient = new Patient(
            Guid.NewGuid(), tenantId, "Paciente de outra empresa", new DateOnly(1990, 1, 1),
            context.Scenario.Clock.GetUtcNow());
        var unit = new ClinicUnit(Guid.NewGuid(), tenantId, "Unidade de outra empresa",
            context.Scenario.Clock.GetUtcNow());
        var appointment = new Appointment(
            Guid.NewGuid(),
            tenantId,
            unit.Id,
            patient.Id,
            context.Professional.Id,
            context.Membership.Id,
            startsAt,
            startsAt.AddHours(1),
            null,
            context.Scenario.Clock.GetUtcNow());
        context.Scenario.Store.Patients.Add(patient);
        context.Scenario.Store.Units.Add(unit);
        context.Scenario.Store.Appointments.Add(appointment);
        return appointment;
    }

    private static AppointmentContext CreateAuthorizedContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var professional = new Professional(
            Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345",
            scenario.Clock.GetUtcNow());
        professional.ConfigureForMembership(
            membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        var patient = new Patient(
            Guid.NewGuid(), tenant.Id, "Paciente da agenda", new DateOnly(1990, 1, 1),
            scenario.Clock.GetUtcNow());
        scenario.Store.Professionals.Add(professional);
        scenario.Store.Patients.Add(patient);
        scenario.SignIn(user, membership);
        return new(scenario, user, tenant, membership, unit, professional, patient);
    }

    private sealed record AppointmentContext(
        IdentityScenario Scenario,
        User User,
        Tenant Tenant,
        TenantMembership Membership,
        ClinicUnit Unit,
        Professional Professional,
        Patient Patient);
}
