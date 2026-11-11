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

    [Fact]
    public void TenantProfileIsNormalizedAndStoredOnExistingTenant()
    {
        var tenant = CreateTenant();

        tenant.CompleteProfile(
            " Clínica Irmãos Saúde Ltda. ",
            " Irmãos Saúde ",
            "contato@irmaossaude.example",
            "(11) 98765-4321");

        Assert.Equal("Clínica Irmãos Saúde Ltda.", tenant.LegalName);
        Assert.Equal("Irmãos Saúde", tenant.Name);
        Assert.Equal("contato@irmaossaude.example", tenant.CommercialEmail);
        Assert.Equal("11987654321", tenant.Phone);
    }

    [Theory]
    [InlineData("invalid-email", "(11) 98765-4321")]
    [InlineData("contato@example.com", "123")]
    public void TenantProfileRejectsInvalidContact(string email, string phone)
    {
        var tenant = CreateTenant();

        Assert.Throws<ArgumentException>(() => tenant.CompleteProfile(
            "Clínica Irmãos Saúde Ltda.", "Irmãos Saúde", email, phone));
    }

    private static Tenant CreateTenant() =>
        new(Guid.NewGuid(), "Empresa de teste", DateTimeOffset.UtcNow);
}
