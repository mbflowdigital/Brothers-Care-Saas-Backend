using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using BrothersCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BrothersCare.ArchitectureTests;

public sealed class TenantSchemaTests
{
    private static readonly Type[] TenantScopedTypes =
    [
        typeof(ClinicUnit),
        typeof(TenantMembership),
        typeof(ClinicUnitMembership),
        typeof(Patient),
        typeof(Professional),
        typeof(PatientCareAssignment),
        typeof(Appointment),
        typeof(ClinicalRecord),
    ];

    [Fact]
    public void TenantScopedEntitiesRequireTenantIdAndTenantScopedAlternateKey()
    {
        using var context = CreateContext();

        foreach (var type in TenantScopedTypes)
        {
            var entity = context.Model.FindEntityType(type);
            Assert.NotNull(entity);

            var tenantId = entity.FindProperty("TenantId");
            Assert.NotNull(tenantId);
            Assert.False(tenantId.IsNullable);
            Assert.Contains(
                entity.GetKeys(),
                key => key.Properties.Select(property => property.Name)
                    .SequenceEqual(["TenantId", "Id"]));
        }
    }

    [Fact]
    public void ClinicalRelationsIncludeTenantIdInForeignKeys()
    {
        using var context = CreateContext();

        AssertTenantScopedForeignKey<Appointment, Patient>(context);
        AssertTenantScopedForeignKey<Appointment, Professional>(context);
        AssertTenantScopedForeignKey<Appointment, ClinicUnit>(context);
        AssertTenantScopedForeignKey<ClinicalRecord, Appointment>(context);
        AssertTenantScopedForeignKey<ClinicalRecord, Patient>(context);
        AssertTenantScopedForeignKey<ClinicalRecord, TenantMembership>(context);
    }

    [Fact]
    public void PlatformAdministratorIsGlobalAndHasOneUser()
    {
        using var context = CreateContext();

        var administrator = context.Model.FindEntityType(typeof(PlatformAdministrator));
        Assert.NotNull(administrator);
        Assert.Null(administrator.FindProperty("TenantId"));
        Assert.Contains(
            administrator.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["UserId"]));
        Assert.Contains(
            administrator.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User) &&
                          foreignKey.Properties.Select(property => property.Name).SequenceEqual(["UserId"]));
    }

    private static BrothersCareDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BrothersCareDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=BrothersCareSchemaTests;Trusted_Connection=True")
            .Options;

        return new BrothersCareDbContext(options);
    }

    private static void AssertTenantScopedForeignKey<TDependent, TPrincipal>(BrothersCareDbContext context)
        where TDependent : class
        where TPrincipal : class
    {
        var dependent = context.Model.FindEntityType(typeof(TDependent));
        Assert.NotNull(dependent);

        Assert.Contains(
            dependent.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal) &&
                          foreignKey.Properties.Select(property => property.Name).Contains("TenantId"));
    }
}
