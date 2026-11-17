using BrothersCare.Application.Scheduling;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Scheduling;

public sealed class ProfessionalAvailabilityTests
{
    [Fact]
    public async Task ProfessionalCanReplaceWeeklyWindowsAndReadUnitTimeZone()
    {
        var context = CreateProfessionalContext();
        ProfessionalAvailabilityWindowInput[] windows =
        [
            new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0)),
            new(DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(11, 0)),
        ];

        var update = await new SetProfessionalWeeklyAvailability(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(context.Professional.Id, context.Unit.Id, windows),
            CancellationToken.None);
        var query = await new GetProfessionalAvailability(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            context.Professional.Id,
            context.Unit.Id,
            context.Scenario.Clock.GetUtcNow(),
            context.Scenario.Clock.GetUtcNow().AddDays(30),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Success, update);
        Assert.Equal(ProfessionalAvailabilityStatus.Success, query.Status);
        Assert.NotNull(query.Availability);
        Assert.True(query.Availability.IsScheduleConfigured);
        Assert.Equal(context.Unit.TimeZoneId, query.Availability.TimeZoneId);
        Assert.Equal(2, query.Availability.WeeklyWindows.Count);
        Assert.Equal(1, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task OverlappingWeeklyWindowsAreRejectedWithoutChangingTheSchedule()
    {
        var context = CreateProfessionalContext();
        ProfessionalAvailabilityWindowInput[] windows =
        [
            new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 30)),
            new(DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(11, 0)),
        ];

        var result = await new SetProfessionalWeeklyAvailability(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(context.Professional.Id, context.Unit.Id, windows),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.InvalidInput, result);
        Assert.Empty(context.Scenario.Store.AvailabilityWindows);
        Assert.Equal(0, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task ProfessionalCannotManageAnotherProfessionalAndClinicAdminCannotCrossTenant()
    {
        var scenario = new IdentityScenario();
        var administrator = scenario.AddUser();
        var (tenant, administratorMembership, unit) = scenario.AddTenant(
            administrator, TenantMembershipRole.ClinicAdministrator);
        var sameTenantProfessional = AddProfessional(scenario, tenant.Id, unit.Id);
        scenario.SignIn(administrator, administratorMembership);
        var setAvailability = new SetProfessionalWeeklyAvailability(
            scenario, scenario.Store, scenario.Clock);

        var sameTenantResult = await setAvailability.ExecuteAsync(
            new(sameTenantProfessional.Professional.Id, unit.Id, []),
            CancellationToken.None);

        var otherUser = scenario.AddUser();
        var (otherTenant, otherMembership, otherUnit) = scenario.AddTenant(otherUser, TenantMembershipRole.Professional);
        var otherProfessional = AddProfessional(
            scenario, otherTenant.Id, otherUnit.Id, otherUser, otherMembership);
        var crossTenantResult = await setAvailability.ExecuteAsync(
            new(otherProfessional.Professional.Id, otherUnit.Id, []),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Success, sameTenantResult);
        Assert.Equal(ProfessionalAvailabilityStatus.ProfessionalNotFound, crossTenantResult);
    }

    [Fact]
    public async Task ProfessionalCannotManageAnotherProfessionalInTheSameTenant()
    {
        var context = CreateProfessionalContext();
        var other = AddProfessional(context.Scenario, context.Tenant.Id, context.Unit.Id);

        var result = await new SetProfessionalWeeklyAvailability(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(other.Professional.Id, context.Unit.Id, []),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Forbidden, result);
        Assert.Equal(0, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task AvailabilityBlockIsRejectedWhenItOverlapsScheduledAppointment()
    {
        var context = CreateProfessionalContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);
        context.Scenario.Store.Appointments.Add(new Appointment(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Unit.Id,
            Guid.NewGuid(),
            context.Professional.Id,
            context.Membership.Id,
            startsAt,
            startsAt.AddHours(1),
            null,
            context.Scenario.Clock.GetUtcNow()));

        var result = await new CreateProfessionalAvailabilityBlock(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(context.Professional.Id, startsAt.AddMinutes(30), startsAt.AddHours(2),
                "Vacation", " Férias "),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.ScheduledAppointmentConflict, result.Status);
        Assert.Empty(context.Scenario.Store.AvailabilityBlocks);
        Assert.Equal(0, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task AvailabilityBlockAdjacentToScheduledAppointmentIsAllowed()
    {
        var context = CreateProfessionalContext();
        var appointmentStart = context.Scenario.Clock.GetUtcNow().AddDays(2);
        var appointmentEnd = appointmentStart.AddHours(1);
        context.Scenario.Store.Appointments.Add(new Appointment(
            Guid.NewGuid(),
            context.Tenant.Id,
            context.Unit.Id,
            Guid.NewGuid(),
            context.Professional.Id,
            context.Membership.Id,
            appointmentStart,
            appointmentEnd,
            null,
            context.Scenario.Clock.GetUtcNow()));

        var result = await new CreateProfessionalAvailabilityBlock(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(context.Professional.Id, appointmentEnd, appointmentEnd.AddHours(1), "Blocked", null),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Success, result.Status);
        Assert.Single(context.Scenario.Store.AvailabilityBlocks);
    }

    [Fact]
    public async Task AvailabilityBlockKindMustUseADeclaredValue()
    {
        var context = CreateProfessionalContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);

        var result = await new CreateProfessionalAvailabilityBlock(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            new(context.Professional.Id, startsAt, startsAt.AddHours(1), "vacation", null),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.InvalidInput, result.Status);
        Assert.Empty(context.Scenario.Store.AvailabilityBlocks);
        Assert.Equal(0, context.Scenario.Store.Commits);
    }

    [Fact]
    public async Task AvailabilityBlockCanBeCreatedListedAndRemoved()
    {
        var context = CreateProfessionalContext();
        var startsAt = context.Scenario.Clock.GetUtcNow().AddDays(2);
        var service = new CreateProfessionalAvailabilityBlock(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock);
        var created = await service.ExecuteAsync(
            new(context.Professional.Id, startsAt, startsAt.AddDays(4),
                "Vacation", " Recesso "),
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Success, created.Status);
        Assert.NotNull(created.Block);
        Assert.Equal("Recesso", created.Block.Reason);

        var query = await new GetProfessionalAvailability(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            context.Professional.Id,
            context.Unit.Id,
            startsAt,
            startsAt.AddDays(5),
            CancellationToken.None);

        Assert.Single(Assert.IsType<ProfessionalAvailabilityData>(query.Availability).Blocks);

        var removed = await new DeleteProfessionalAvailabilityBlock(
            context.Scenario, context.Scenario.Store, context.Scenario.Clock).ExecuteAsync(
            context.Professional.Id,
            created.Block.Id,
            CancellationToken.None);

        Assert.Equal(ProfessionalAvailabilityStatus.Success, removed);
        Assert.Empty(context.Scenario.Store.AvailabilityBlocks);
        Assert.Equal(2, context.Scenario.Store.Commits);
    }

    private static ProfessionalContext CreateProfessionalContext()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, membership, unit) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        var profile = new Professional(Guid.NewGuid(), tenant.Id, user.DisplayName, "Fisioterapia", "12345",
            scenario.Clock.GetUtcNow());
        profile.ConfigureForMembership(membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        scenario.Store.Professionals.Add(profile);
        scenario.SignIn(user, membership);
        return new(scenario, tenant, membership, unit, profile);
    }

    private static (TenantMembership Membership, Professional Professional) AddProfessional(
        IdentityScenario scenario,
        Guid tenantId,
        Guid clinicUnitId,
        BrothersCare.Domain.Identity.User? existingUser = null,
        TenantMembership? existingMembership = null)
    {
        var user = existingUser ?? scenario.AddUser();
        var membership = existingMembership ?? new TenantMembership(
            Guid.NewGuid(), tenantId, user.Id, TenantMembershipRole.Professional, scenario.Clock.GetUtcNow());
        var profile = new Professional(Guid.NewGuid(), tenantId, user.DisplayName, "Fisioterapia", "12345",
            scenario.Clock.GetUtcNow());
        profile.ConfigureForMembership(membership.Id, user.DisplayName, "Fisioterapia", "CREFITO", "12345");
        if (existingMembership is null)
        {
            scenario.Store.Memberships.Add(membership);
            scenario.Store.UnitMemberships.Add(new ClinicUnitMembership(
                Guid.NewGuid(), tenantId, clinicUnitId, membership.Id, scenario.Clock.GetUtcNow()));
        }
        scenario.Store.Professionals.Add(profile);
        return (membership, profile);
    }

    private sealed record ProfessionalContext(
        IdentityScenario Scenario,
        Tenant Tenant,
        TenantMembership Membership,
        ClinicUnit Unit,
        Professional Professional);
}
