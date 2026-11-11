using BrothersCare.Application.Onboarding;
using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Onboarding;

public sealed class CompleteOnboardingTests
{
    [Fact]
    public async Task ValidRequestCompletesEveryExistingAggregateInOneCommit()
    {
        var scenario = new CompletionScenario();

        var result = await scenario.ExecuteAsync();

        Assert.Equal(OnboardingCompletionStatus.Completed, result.Status);
        Assert.Equal(scenario.Tenant.Id, result.TenantId);
        Assert.Equal(TenantStatus.Active, scenario.Tenant.Status);
        Assert.Equal(MembershipStatus.Active, scenario.Membership.Status);
        Assert.Equal(UserCredentialStatus.Active, scenario.Credential.Status);
        Assert.Equal("52998224725", scenario.User.Cpf);
        Assert.Equal("Administradora da Clínica", scenario.User.DisplayName);
        Assert.NotNull(scenario.Token.ConsumedAt);
        var unit = Assert.Single(scenario.Store.Units);
        Assert.True(unit.IsDefault);
        Assert.Equal("01310100", unit.PostalCode);
        var unitMembership = Assert.Single(scenario.Store.UnitMemberships);
        Assert.Equal(unit.Id, unitMembership.ClinicUnitId);
        Assert.Equal(scenario.Membership.Id, unitMembership.TenantMembershipId);
        Assert.Equal(1, scenario.Store.Commits);
        Assert.Equal([AuthenticationAuditEvent.OnboardingCompleted], scenario.Audit.Events);
    }

    [Fact]
    public async Task ConsumedTokenCannotCompleteTwice()
    {
        var scenario = new CompletionScenario();
        await scenario.ExecuteAsync();

        var repeated = await scenario.ExecuteAsync();

        Assert.Equal(OnboardingCompletionStatus.UsedToken, repeated.Status);
        Assert.Equal(1, scenario.Store.Commits);
        Assert.Single(scenario.Store.Units);
    }

    [Fact]
    public async Task ExpiredTokenDoesNotChangeOnboardingState()
    {
        var scenario = new CompletionScenario(expired: true);

        var result = await scenario.ExecuteAsync();

        Assert.Equal(OnboardingCompletionStatus.ExpiredToken, result.Status);
        Assert.Equal(TenantStatus.PendingOnboarding, scenario.Tenant.Status);
        Assert.Empty(scenario.Store.Units);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task CpfAlreadyOwnedByAnotherUserIsRejected()
    {
        var scenario = new CompletionScenario();
        var other = new User(Guid.NewGuid(), "other@example.test", "Outra pessoa", scenario.Clock.GetUtcNow());
        other.SetCpf("52998224725");
        scenario.Store.OtherUsers.Add(other);

        var result = await scenario.ExecuteAsync();

        Assert.Equal(OnboardingCompletionStatus.CpfUnavailable, result.Status);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task ActiveGlobalCredentialMustBeConfirmedAndIsNeverReset()
    {
        var scenario = new CompletionScenario(activeCredentialPassword: "ExistingPassword123");

        var result = await scenario.ExecuteAsync(password: "DifferentPassword123");

        Assert.Equal(OnboardingCompletionStatus.InvalidCredentials, result.Status);
        Assert.Equal(UserCredentialStatus.Active, scenario.Credential.Status);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task DevelopmentBypassUsesConfiguredFirstAccessPassword()
    {
        var scenario = new CompletionScenario();

        var result = await scenario.ExecuteAsync("Aabbcc123!", new DevelopmentOnboardingBypass("Aabbcc123!"));

        Assert.Equal(OnboardingCompletionStatus.Completed, result.Status);
        Assert.NotNull(scenario.Credential.PasswordHash);
        Assert.True(scenario.Hasher.Verify(scenario.User, scenario.Credential.PasswordHash!, "Aabbcc123!"));
    }

    [Fact]
    public async Task DevelopmentBypassRejectsAnyOtherWeakPassword()
    {
        var scenario = new CompletionScenario();

        var result = await scenario.ExecuteAsync("weak", new DevelopmentOnboardingBypass("Aabbcc123!"));

        Assert.Equal(OnboardingCompletionStatus.InvalidInput, result.Status);
        Assert.Null(scenario.Credential.PasswordHash);
    }

    private sealed class CompletionScenario
    {
        public CompletionScenario(bool expired = false, string? activeCredentialPassword = null)
        {
            var now = Clock.GetUtcNow();
            var generated = Tokens.Generate();
            User = new User(Guid.NewGuid(), "admin@example.test", "Responsável", now);
            Credential = new UserCredential(Guid.NewGuid(), User.Id, now);
            if (activeCredentialPassword is not null)
            {
                Credential.Activate(Hasher.Hash(User, activeCredentialPassword), now);
            }

            Tenant = Tenant.CreatePendingOnboarding(Guid.NewGuid(), "Clínica inicial", "11222333000181", now);
            Membership = TenantMembership.CreateInvitation(
                Guid.NewGuid(), Tenant.Id, User.Id, TenantMembershipRole.ClinicAdministrator, now);
            Token = new AccountActionToken(
                Guid.NewGuid(), User.Id, AccountActionTokenPurpose.CommercialOnboarding,
                generated.SecretHash, expired ? now.AddHours(-1) : now,
                expired ? now.AddMinutes(-1) : now.AddMinutes(30), Membership.Id);
            Secret = generated.Secret;
            Store = new CompletionStore(User, Credential, Tenant, Membership, Token);
        }

        public TestClock Clock { get; } = new();
        public TestTokenGenerator Tokens { get; } = new();
        public TestPasswordHasher Hasher { get; } = new();
        public TestAudit Audit { get; } = new();
        public User User { get; }
        public UserCredential Credential { get; }
        public Tenant Tenant { get; }
        public TenantMembership Membership { get; }
        public AccountActionToken Token { get; }
        public string Secret { get; }
        public CompletionStore Store { get; }

        public Task<OnboardingCompletionResult> ExecuteAsync(string password = "SecurePassword123", DevelopmentOnboardingBypass? bypass = null) =>
            new CompleteOnboarding(Store, Hasher, Tokens, Clock, Audit, bypass).ExecuteAsync(new(
                Secret,
                new("Administradora da Clínica", "529.982.247-25", password, password),
                new("Clínica Exemplo Ltda.", "Clínica Exemplo", "contato@example.test", "(11) 98765-4321"),
                new("Unidade Matriz", "01310-100", "Avenida Paulista", "1000", null,
                    "Bela Vista", "São Paulo", "SP", "(11) 3333-4444")), CancellationToken.None);
    }

    private sealed class TestAudit : IAuthenticationAudit
    {
        public List<AuthenticationAuditEvent> Events { get; } = [];

        public void Record(AuthenticationAuditEvent action, DateTimeOffset occurredAt) => Events.Add(action);
    }

    private sealed class CompletionStore(
        User user,
        UserCredential credential,
        Tenant tenant,
        TenantMembership membership,
        AccountActionToken token) : IOnboardingCompletionStore, IOnboardingCompletionTransaction
    {
        public List<User> OtherUsers { get; } = [];
        public List<ClinicUnit> Units { get; } = [];
        public List<ClinicUnitMembership> UnitMemberships { get; } = [];
        public int Commits { get; private set; }

        public Task<IOnboardingCompletionTransaction> BeginAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IOnboardingCompletionTransaction>(this);

        public Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken) =>
            Task.FromResult<AccountActionToken?>(token.SecretHash.SequenceEqual(secretHash) ? token : null);

        public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user.Id == userId ? user : null);

        public Task<User?> FindUserByCpfAsync(string cpf, CancellationToken cancellationToken) =>
            Task.FromResult(OtherUsers.Append(user).SingleOrDefault(item => item.Cpf == cpf));

        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserCredential?>(credential.UserId == userId ? credential : null);

        public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Tenant?>(tenant.Id == tenantId ? tenant : null);

        public Task<TenantMembership?> FindMembershipAsync(
            Guid membershipId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<TenantMembership?>(
                membership.Id == membershipId && membership.UserId == userId ? membership : null);

        public Task<bool> HasClinicUnitAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(Units.Any(item => item.TenantId == tenantId));

        public void AddClinicUnit(ClinicUnit clinicUnit) => Units.Add(clinicUnit);

        public void AddClinicUnitMembership(ClinicUnitMembership unitMembership) =>
            UnitMemberships.Add(unitMembership);

        public Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Commits++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
