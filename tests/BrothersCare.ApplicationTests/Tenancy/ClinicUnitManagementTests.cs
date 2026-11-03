using BrothersCare.Application.Tenancy;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class ClinicUnitManagementTests
{
    [Fact]
    public async Task AdministratorCreatesAndRenamesUnitInActiveTenant()
    {
        var scenario = CreateAdministratorScenario();
        var service = Service(scenario);

        var created = await service.CreateAsync(" Unidade Centro ", CancellationToken.None);
        var renamed = await service.RenameAsync(created.Unit!.Id, "Unidade Matriz", CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.Success, created.Status);
        Assert.Equal(ClinicUnitOperationStatus.Success, renamed.Status);
        Assert.Equal("Unidade Matriz", renamed.Unit!.Name);
        Assert.Contains(scenario.Store.Units, item => item.Id == created.Unit.Id && item.Name == "Unidade Matriz");
    }

    [Fact]
    public async Task AdministratorCannotCreateDuplicateUnitNameInSameTenant()
    {
        var scenario = CreateAdministratorScenario();

        var result = await Service(scenario).CreateAsync("Unidade de teste", CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.NameAlreadyExists, result.Status);
    }

    [Fact]
    public async Task ProfessionalCannotManageUnits()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(user, TenantMembershipRole.Professional);
        scenario.SignIn(user, membership);

        var result = await Service(scenario).CreateAsync("Unidade Centro", CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task UnitFromAnotherTenantIsNotFound()
    {
        var scenario = CreateAdministratorScenario();
        var otherUser = scenario.AddUser(email: "other@example.invalid");
        var other = scenario.AddTenant(otherUser);

        var result = await Service(scenario).GetAsync(other.Unit.Id, CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ListRejectsInvalidPage()
    {
        var scenario = CreateAdministratorScenario();

        var result = await Service(scenario).ListAsync(null, 0, 25, CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.InvalidInput, result.Status);
    }

    [Fact]
    public async Task DeactivationCancelsOnlyFutureScheduledAppointmentsAndPreservesHistory()
    {
        var scenario = CreateAdministratorScenario();
        var unit = scenario.Store.Units[0];
        var now = scenario.Clock.GetUtcNow();
        var future = CreateAppointment(unit, now.AddDays(1));
        var past = CreateAppointment(unit, now.AddDays(-1));
        var completed = CreateAppointment(unit, now.AddDays(2));
        completed.MarkCompleted();
        scenario.Store.Appointments.AddRange([future, past, completed]);

        var result = await Service(scenario).DeactivateAsync(unit.Id, CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.Success, result.Status);
        Assert.Equal(1, result.CancelledAppointments);
        Assert.False(unit.IsActive);
        Assert.Equal(AppointmentStatus.Cancelled, future.Status);
        Assert.Equal(AppointmentStatus.Scheduled, past.Status);
        Assert.Equal(AppointmentStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task DefaultUnitCannotBeDeactivated()
    {
        var scenario = CreateAdministratorScenario();
        var unit = scenario.Store.Units[0];
        unit.MarkAsDefault();

        var result = await Service(scenario).DeactivateAsync(unit.Id, CancellationToken.None);

        Assert.Equal(ClinicUnitOperationStatus.DefaultUnitCannotBeDeactivated, result.Status);
        Assert.True(unit.IsActive);
        Assert.True(unit.IsDefault);
    }

    private static ManageClinicUnits Service(IdentityScenario scenario) => new(scenario, scenario.Store, scenario.Clock);

    private static IdentityScenario CreateAdministratorScenario()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        return scenario;
    }

    private static Appointment CreateAppointment(ClinicUnit unit, DateTimeOffset startsAt) =>
        new(Guid.NewGuid(), unit.TenantId, unit.Id, Guid.NewGuid(), Guid.NewGuid(), startsAt,
            startsAt.AddHours(1), startsAt.AddDays(-1));
}
