using BrothersCare.Application.Tenancy;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class CommercialOnboardingTests
{
    [Fact]
    public async Task CreatesPendingCompanyAndSendsInitialInvitation()
    {
        var scenario = new IdentityScenario();

        var result = await Create(scenario).ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(CommercialOnboardingStatus.Created, result.Status);
        var tenant = Assert.Single(scenario.Store.Tenants);
        Assert.Equal("11222333000181", tenant.Cnpj);
        Assert.Equal(TenantStatus.PendingOnboarding, tenant.Status);
        Assert.Empty(scenario.Store.Units);
        var user = Assert.Single(scenario.Store.Users);
        Assert.Null(user.Cpf);
        Assert.Equal(UserCredentialStatus.PendingActivation, Assert.Single(scenario.Store.Credentials).Status);
        var membership = Assert.Single(scenario.Store.Memberships);
        Assert.Equal(TenantMembershipRole.ClinicAdministrator, membership.Role);
        Assert.Equal(MembershipStatus.Invited, membership.Status);
        var token = Assert.Single(scenario.Store.Tokens);
        Assert.Equal(AccountActionTokenPurpose.CommercialOnboarding, token.Purpose);
        Assert.Equal(membership.Id, token.TenantMembershipId);
        Assert.True(scenario.Tokens.Matches(Assert.Single(scenario.Invitations).Token, token.SecretHash));
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Fact]
    public async Task DoesNotCreateAnotherCompanyForRegisteredCnpj()
    {
        var scenario = new IdentityScenario();
        Assert.Equal(CommercialOnboardingStatus.Created,
            (await Create(scenario).ExecuteAsync(Command(), CancellationToken.None)).Status);

        var result = await Create(scenario).ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(CommercialOnboardingStatus.CnpjAlreadyRegistered, result.Status);
        Assert.Single(scenario.Store.Tenants);
        Assert.Single(scenario.Invitations);
    }

    [Fact]
    public async Task RejectsInvalidCommercialDataWithoutSendingEmail()
    {
        var scenario = new IdentityScenario();

        var result = await Create(scenario).ExecuteAsync(Command(cnpj: "00.000.000/0000-00"), CancellationToken.None);

        Assert.Equal(CommercialOnboardingStatus.InvalidInput, result.Status);
        Assert.Empty(scenario.Store.Tenants);
        Assert.Empty(scenario.Invitations);
    }

    [Fact]
    public async Task AcceptsAValidFormattedCnpj()
    {
        var scenario = new IdentityScenario();

        var result = await Create(scenario).ExecuteAsync(
            Command(cnpj: "38.416.176/0001-00"), CancellationToken.None);

        Assert.Equal(CommercialOnboardingStatus.Created, result.Status);
    }

    [Fact]
    public async Task CreatesPreRegistrationAndTokenWhenEmailDeliveryIsNotConfigured()
    {
        var scenario = new IdentityScenario();
        var useCase = new CreateCommercialOnboarding(
            scenario.Store, scenario.Tokens, invitationSender: null, scenario.Clock);

        var result = await useCase.ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(CommercialOnboardingStatus.Created, result.Status);
        Assert.NotNull(result.DevelopmentToken);
        Assert.Single(scenario.Store.Tenants);
        Assert.Single(scenario.Store.Tokens);
        Assert.Empty(scenario.Invitations);
        Assert.Equal(1, scenario.Store.Commits);
    }

    [Fact]
    public async Task ResendRevokesThePreviousTokenAndSendsANewInvitation()
    {
        var scenario = new IdentityScenario();
        var created = await Create(scenario).ExecuteAsync(Command(), CancellationToken.None);
        var previousToken = Assert.Single(scenario.Store.Tokens);

        var result = await Resend(scenario).ExecuteAsync(created.TenantId!.Value, CancellationToken.None);

        Assert.Equal(CommercialOnboardingResendStatus.Accepted, result.Status);
        Assert.NotNull(result.Token);
        Assert.NotNull(previousToken.RevokedAt);
        Assert.Equal(2, scenario.Invitations.Count);
        var activeToken = Assert.Single(scenario.Store.Tokens,
            token => token.Purpose == AccountActionTokenPurpose.CommercialOnboarding && token.RevokedAt is null);
        Assert.True(scenario.Tokens.Matches(result.Token!, activeToken.SecretHash));
    }

    [Fact]
    public async Task RegeneratesOnboardingLinkWhenEmailDeliveryIsNotConfigured()
    {
        var scenario = new IdentityScenario();
        var created = await Create(scenario).ExecuteAsync(Command(), CancellationToken.None);
        var previousToken = Assert.Single(scenario.Store.Tokens);
        var useCase = new ResendCommercialOnboardingInvitation(
            scenario.Store, scenario.Tokens, invitationSender: null, scenario.Clock);

        var result = await useCase.ExecuteAsync(created.TenantId!.Value, CancellationToken.None);

        Assert.Equal(CommercialOnboardingResendStatus.Accepted, result.Status);
        Assert.NotNull(result.Token);
        Assert.NotNull(previousToken.RevokedAt);
        Assert.Single(scenario.Invitations);
        Assert.Equal(2, scenario.Store.Commits);
    }

    private static CreateCommercialOnboarding Create(IdentityScenario scenario) =>
        new(scenario.Store, scenario.Tokens, scenario, scenario.Clock);

    private static ResendCommercialOnboardingInvitation Resend(IdentityScenario scenario) =>
        new(scenario.Store, scenario.Tokens, scenario, scenario.Clock);

    private static CreateCommercialOnboardingCommand Command(
        string cnpj = "11.222.333/0001-81") =>
        new("Clínica Exemplo", cnpj, "Pessoa Responsável", "responsavel@example.invalid");
}
