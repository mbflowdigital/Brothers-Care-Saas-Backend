using BrothersCare.Application.Onboarding;
using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.ApplicationTests.Support;

namespace BrothersCare.ApplicationTests.Onboarding;

public sealed class GetOnboardingContextTests
{
    [Fact]
    public async Task ValidTokenReturnsOnlyTheCommercialContext()
    {
        var clock = new TestClock();
        var tokenGenerator = new TestTokenGenerator();
        var token = tokenGenerator.Generate();
        var source = new StubSource(new(CommercialOnboardingLookupStatus.Ready, new(
            Guid.NewGuid(), "admin@example.test", "11222333000181", "Clínica Exemplo",
            clock.GetUtcNow().AddMinutes(30))));
        var useCase = new GetOnboardingContext(source, tokenGenerator, clock);

        var result = await useCase.ExecuteAsync(token.Secret, "11.222.333/0001-81", CancellationToken.None);

        Assert.Equal(OnboardingContextStatus.Ready, result.Status);
        Assert.Equal("admin@example.test", result.Context?.Email);
        Assert.Equal("11222333000181", result.Context?.Cnpj);
        Assert.NotNull(source.ReceivedHash);
        Assert.NotEqual(token.Secret, Convert.ToBase64String(source.ReceivedHash!));
    }

    [Theory]
    [InlineData(CommercialOnboardingLookupStatus.Invalid, OnboardingContextStatus.Invalid)]
    [InlineData(CommercialOnboardingLookupStatus.Expired, OnboardingContextStatus.Expired)]
    [InlineData(CommercialOnboardingLookupStatus.Revoked, OnboardingContextStatus.Revoked)]
    [InlineData(CommercialOnboardingLookupStatus.Used, OnboardingContextStatus.Used)]
    [InlineData(CommercialOnboardingLookupStatus.Unavailable, OnboardingContextStatus.Unavailable)]
    public async Task SourceStateIsMappedWithoutLeakingARecord(
        CommercialOnboardingLookupStatus sourceStatus,
        OnboardingContextStatus expectedStatus)
    {
        var tokenGenerator = new TestTokenGenerator();
        var useCase = new GetOnboardingContext(
            new StubSource(new(sourceStatus)), tokenGenerator, new TestClock());

        var result = await useCase.ExecuteAsync(tokenGenerator.Generate().Secret, "11.222.333/0001-81", CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Context);
    }

    [Fact]
    public async Task InvalidTokenIsRejectedBeforeTheCommercialSource()
    {
        var source = new StubSource(new(CommercialOnboardingLookupStatus.Ready));
        var useCase = new GetOnboardingContext(source, new TestTokenGenerator(), new TestClock());

        var result = await useCase.ExecuteAsync("not-a-valid-token", "11.222.333/0001-81", CancellationToken.None);

        Assert.Equal(OnboardingContextStatus.Invalid, result.Status);
        Assert.Null(source.ReceivedHash);
    }

    [Fact]
    public async Task RecordPastItsExpirationIsRejectedEvenWhenSourceReportsReady()
    {
        var clock = new TestClock();
        var tokenGenerator = new TestTokenGenerator();
        var source = new StubSource(new(CommercialOnboardingLookupStatus.Ready, new(
            Guid.NewGuid(), "admin@example.test", "11222333000181", null,
            clock.GetUtcNow().AddSeconds(-1))));
        var useCase = new GetOnboardingContext(source, tokenGenerator, clock);

        var result = await useCase.ExecuteAsync(tokenGenerator.Generate().Secret, "11.222.333/0001-81", CancellationToken.None);

        Assert.Equal(OnboardingContextStatus.Expired, result.Status);
        Assert.Null(result.Context);
    }

    [Fact]
    public async Task MismatchedCnpjDoesNotRevealTheOnboardingContext()
    {
        var clock = new TestClock();
        var tokenGenerator = new TestTokenGenerator();
        var token = tokenGenerator.Generate();
        var source = new StubSource(new(CommercialOnboardingLookupStatus.Ready, new(
            Guid.NewGuid(), "admin@example.test", "11222333000181", "Clínica Exemplo",
            clock.GetUtcNow().AddMinutes(30))));
        var useCase = new GetOnboardingContext(source, tokenGenerator, clock);

        var result = await useCase.ExecuteAsync(token.Secret, "38.416.176/0001-00", CancellationToken.None);

        Assert.Equal(OnboardingContextStatus.CnpjMismatch, result.Status);
        Assert.Null(result.Context);
    }

    private sealed class StubSource(CommercialOnboardingLookup result) : ICommercialOnboardingSource
    {
        public byte[]? ReceivedHash { get; private set; }

        public Task<CommercialOnboardingLookup> FindAsync(byte[] tokenHash, CancellationToken cancellationToken)
        {
            ReceivedHash = (byte[])tokenHash.Clone();
            return Task.FromResult(result);
        }
    }
}
