using BrothersCare.Domain.Care;

namespace BrothersCare.DomainTests.Care;

public sealed class ProfessionalProfileTests
{
    [Fact]
    public void ProfessionalMayStartWithOnlyRequiredTechnicalLinks()
    {
        var professional = new Professional(Guid.NewGuid(), Guid.NewGuid(), "Marina Lima", null, null,
            DateTimeOffset.UtcNow);
        var membershipId = Guid.NewGuid();

        professional.ConfigureForMembership(membershipId, "Marina Lima", null, null, null);

        Assert.Equal(membershipId, professional.TenantMembershipId);
        Assert.Equal(string.Empty, professional.Specialty);
        Assert.Null(professional.CouncilType);
        Assert.Null(professional.CouncilState);
        Assert.Equal(string.Empty, professional.RegistrationNumber);
        Assert.Null(professional.DefaultAppointmentDurationMinutes);
        Assert.Equal(ProfessionalStatus.Active, professional.Status);
    }

    [Fact]
    public void ProfessionalStoresMinimalSchedulingDefaults()
    {
        var professional = new Professional(Guid.NewGuid(), Guid.NewGuid(), "Marina Lima", null, null,
            DateTimeOffset.UtcNow);

        professional.ConfigureForMembership(Guid.NewGuid(), "Marina Lima", "Psicologia", "CRP", "123456", "sp", 50);

        Assert.Equal("Psicologia", professional.Specialty);
        Assert.Equal("CRP", professional.CouncilType);
        Assert.Equal("SP", professional.CouncilState);
        Assert.Equal("123456", professional.RegistrationNumber);
        Assert.Equal(50, professional.DefaultAppointmentDurationMinutes);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(481)]
    public void AppointmentDurationOutsideSimpleOperationalRangeIsRejected(int duration)
    {
        var professional = new Professional(Guid.NewGuid(), Guid.NewGuid(), "Marina Lima", null, null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => professional.ConfigureForMembership(
            Guid.NewGuid(), "Marina Lima", null, null, null, null, duration));
    }
}
