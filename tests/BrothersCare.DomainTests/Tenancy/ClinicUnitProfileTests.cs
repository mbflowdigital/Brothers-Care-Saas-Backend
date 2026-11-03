using BrothersCare.Domain.Tenancy;

namespace BrothersCare.DomainTests.Tenancy;

public sealed class ClinicUnitProfileTests
{
    [Fact]
    public void ContactAddressAndDefaultStateAreNormalized()
    {
        var unit = CreateUnit();

        unit.ConfigureContactAndAddress(
            "01310-100", "Avenida Paulista", "1000", "Conjunto 10",
            "Bela Vista", "São Paulo", "sp", "(11) 3333-4444");
        unit.MarkAsDefault();

        Assert.Equal("01310100", unit.PostalCode);
        Assert.Equal("Avenida Paulista", unit.Street);
        Assert.Equal("1000", unit.Number);
        Assert.Equal("Conjunto 10", unit.Complement);
        Assert.Equal("Bela Vista", unit.District);
        Assert.Equal("São Paulo", unit.City);
        Assert.Equal("SP", unit.State);
        Assert.Equal("1133334444", unit.Phone);
        Assert.True(unit.IsDefault);
    }

    [Fact]
    public void DefaultUnitCannotBeDeactivated()
    {
        var unit = CreateUnit();
        unit.MarkAsDefault();

        Assert.Throws<InvalidOperationException>(unit.Deactivate);
    }

    [Theory]
    [InlineData("123", "SP", "(11) 3333-4444")]
    [InlineData("01310-100", "S", "(11) 3333-4444")]
    [InlineData("01310-100", "SP", "123")]
    public void ContactAddressRejectsInvalidValues(string postalCode, string state, string phone)
    {
        var unit = CreateUnit();

        Assert.Throws<ArgumentException>(() => unit.ConfigureContactAndAddress(
            postalCode, "Avenida Paulista", "1000", null,
            "Bela Vista", "São Paulo", state, phone));
    }

    private static ClinicUnit CreateUnit() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Unidade Matriz", DateTimeOffset.UtcNow);
}
