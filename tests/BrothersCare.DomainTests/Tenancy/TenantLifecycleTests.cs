using BrothersCare.Domain.Tenancy;

namespace BrothersCare.DomainTests.Tenancy;

public sealed class TenantLifecycleTests
{
    [Fact]
    public void ActiveTenantCanBeSuspendedAndReactivated()
    {
        var tenant = CreateTenant();

        tenant.Suspend();
        tenant.Reactivate();

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Theory]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.Suspended)]
    public void ActiveOrSuspendedTenantCanBeDeactivated(TenantStatus initialStatus)
    {
        var tenant = CreateTenant();
        if (initialStatus is TenantStatus.Suspended)
        {
            tenant.Suspend();
        }

        tenant.Deactivate();

        Assert.Equal(TenantStatus.Deactivated, tenant.Status);
    }

    [Fact]
    public void DeactivatedTenantCannotBeSuspendedOrReactivated()
    {
        var tenant = CreateTenant();
        tenant.Deactivate();

        Assert.Throws<InvalidOperationException>(tenant.Suspend);
        Assert.Throws<InvalidOperationException>(tenant.Reactivate);
    }

    private static Tenant CreateTenant() =>
        new(Guid.NewGuid(), "Empresa de teste", DateTimeOffset.UtcNow);
}
