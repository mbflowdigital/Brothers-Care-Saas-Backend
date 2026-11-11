using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.DomainTests.Identity;

public sealed class BrazilianDocumentTests
{
    [Fact]
    public void UserNormalizesValidCpf()
    {
        var user = new User(Guid.NewGuid(), "person@example.invalid", "Pessoa", DateTimeOffset.UtcNow);

        user.SetCpf("529.982.247-25");

        Assert.Equal("52998224725", user.Cpf);
    }

    [Fact]
    public void TenantNormalizesValidCnpj()
    {
        var tenant = new Tenant(Guid.NewGuid(), "Empresa", DateTimeOffset.UtcNow);

        tenant.SetCnpj("11.222.333/0001-81");

        Assert.Equal("11222333000181", tenant.Cnpj);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("529.982.247-24")]
    public void UserRejectsInvalidCpf(string cpf)
    {
        var user = new User(Guid.NewGuid(), "person@example.invalid", "Pessoa", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => user.SetCpf(cpf));
    }

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("11.222.333/0001-80")]
    public void TenantRejectsInvalidCnpj(string cnpj)
    {
        var tenant = new Tenant(Guid.NewGuid(), "Empresa", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => tenant.SetCnpj(cnpj));
    }
}
