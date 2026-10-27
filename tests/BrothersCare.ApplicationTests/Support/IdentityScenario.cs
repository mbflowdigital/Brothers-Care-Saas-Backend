using System.Security.Cryptography;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Sessions;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Support;

internal sealed class IdentityScenario : ICurrentPrincipalAccessor, IAuthenticationAudit, IInvitationSender, IPasswordRecoverySender
{
    public IdentityMemoryStore Store { get; } = new();
    public TestClock Clock { get; } = new();
    public TestPasswordHasher Hasher { get; } = new();
    public TestTokenGenerator Tokens { get; } = new();
    public AuthenticationSettings Settings { get; } = new();
    public CurrentPrincipal? Principal { get; set; }
    public List<AuthenticationAuditEvent> Events { get; } = [];
    public List<InvitationDelivery> Invitations { get; } = [];
    public List<PasswordRecoveryDelivery> Recoveries { get; } = [];

    public User AddUser(bool active = true, string email = "user@example.invalid")
    {
        var user = new User(Guid.NewGuid(), email, "Pessoa de teste", Clock.GetUtcNow());
        var credential = new UserCredential(Guid.NewGuid(), user.Id, Clock.GetUtcNow());
        if (active)
        {
            credential.Activate(Hasher.Hash(user, "TestPassword123"), Clock.GetUtcNow());
        }
        Store.Users.Add(user);
        Store.Credentials.Add(credential);
        return user;
    }

    public string SignIn(User user, TenantMembership? membership = null)
    {
        var generated = Tokens.Generate();
        var now = Clock.GetUtcNow();
        var session = new UserSession(Guid.NewGuid(), user.Id, generated.SecretHash, now, now.AddMinutes(30), now.AddHours(12));
        if (membership is not null)
        {
            session.SelectActiveTenant(membership, now);
        }
        Store.Sessions.Add(session);
        Principal = new CurrentPrincipal(user.Id, session.Id, session.ActiveTenantId,
            Store.Administrators.Any(item => item.UserId == user.Id) ? "SuperAdministrator" : null,
            membership?.Id, membership?.Role.ToString(), []);
        return $"{session.Id:D}.{generated.Secret}";
    }

    public (Tenant Tenant, TenantMembership Membership, ClinicUnit Unit) AddTenant(User user,
        TenantMembershipRole role = TenantMembershipRole.ClinicAdministrator)
    {
        var now = Clock.GetUtcNow();
        var tenant = new Tenant(Guid.NewGuid(), "Empresa de teste", now);
        var unit = new ClinicUnit(Guid.NewGuid(), tenant.Id, "Unidade de teste", now);
        var membership = new TenantMembership(Guid.NewGuid(), tenant.Id, user.Id, role, now);
        Store.Tenants.Add(tenant);
        Store.Units.Add(unit);
        Store.Memberships.Add(membership);
        Store.UnitMemberships.Add(new ClinicUnitMembership(Guid.NewGuid(), tenant.Id, unit.Id, membership.Id, now));
        return (tenant, membership, unit);
    }

    public string AddToken(User user, AccountActionTokenPurpose purpose, Guid? membershipId = null)
    {
        var generated = Tokens.Generate();
        var now = Clock.GetUtcNow();
        Store.Tokens.Add(new AccountActionToken(Guid.NewGuid(), user.Id, purpose, generated.SecretHash, now,
            now.AddMinutes(30), membershipId));
        return generated.Secret;
    }

    // EF can hydrate Suspended from persistence; no application suspension workflow exists yet.
    public static void LoadSuspendedMembership(TenantMembership membership) =>
        typeof(TenantMembership).GetProperty(nameof(TenantMembership.Status))!.SetValue(membership, MembershipStatus.Suspended);

    public void Record(AuthenticationAuditEvent action, DateTimeOffset occurredAt) => Events.Add(action);
    public Task SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken)
    {
        Invitations.Add(invitation);
        return Task.CompletedTask;
    }

    public Task<bool> TrySendAsync(PasswordRecoveryDelivery delivery, CancellationToken cancellationToken)
    {
        Recoveries.Add(delivery);
        return Task.FromResult(true);
    }
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}

// Test doubles isolate application decisions; they do not validate production cryptography.
internal sealed class TestPasswordHasher : IPasswordHasher
{
    private readonly Dictionary<string, (Guid UserId, string Password)> values = [];
    public int DummyChecks { get; private set; }
    public string Hash(User user, string password)
    {
        var value = Guid.NewGuid().ToString("N");
        values.Add(value, (user.Id, password));
        return value;
    }

    public bool Verify(User user, string passwordHash, string password) =>
        values.TryGetValue(passwordHash, out var value) && value == (user.Id, password);
    public void VerifyDummy(string password) => DummyChecks++;
}

internal sealed class TestTokenGenerator : ITokenGenerator
{
    public GeneratedActionToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return new GeneratedActionToken(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            SHA256.HashData(bytes));
    }

    public bool TryHash(string secret, out byte[] secretHash)
    {
        secretHash = [];
        if (secret is null || secret.Length != 43 || !secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }
        secretHash = SHA256.HashData(Convert.FromBase64String(secret.Replace('-', '+').Replace('_', '/') + "="));
        return true;
    }

    public bool Matches(string secret, byte[] expectedHash) =>
        TryHash(secret, out var hash) && CryptographicOperations.FixedTimeEquals(hash, expectedHash);
}
