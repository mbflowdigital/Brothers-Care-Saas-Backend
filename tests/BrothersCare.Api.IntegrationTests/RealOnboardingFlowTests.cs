using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BrothersCare.Api.IntegrationTests;

public sealed class RealOnboardingFlowTests
{
    [RealDatabaseFact]
    public async Task RealDatabaseFlowIsAtomicIdempotentAndAllowsImmediateLogin()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONBOARDING_TEST_CONNECTION_STRING")!;

        await using var factory = new RealOnboardingFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var createdTenants = new List<Guid>();

        try
        {
            await CleanupStrandedTestDataAsync(factory);
            var csrf = await GetCsrfAsync(client);
            var successful = TestIdentity.Create();
            var tenantId = await CreateAsync(client, csrf, successful);
            createdTenants.Add(tenantId);
            var token = factory.Sender.TakeToken(successful.Email);

            using (var duplicate = await PostAsync(client, "/api/v1/commercial-onboardings/simulated-payment",
                       successful.CommercialRequest, csrf))
            {
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            }

            using (var context = await PostAsync(client, "/api/v1/onboarding/context", new { token }, csrf))
            {
                Assert.Equal(HttpStatusCode.OK, context.StatusCode);
            }

            using (var completion = await PostAsync(client, "/api/v1/onboarding/completion",
                       successful.CompletionRequest(token), csrf))
            {
                Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
            }

            using (var repeated = await PostAsync(client, "/api/v1/onboarding/completion",
                       successful.CompletionRequest(token), csrf))
            {
                Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
                Assert.Equal("used-onboarding-token", await ReadProblemTypeAsync(repeated));
            }

            using (var usedContext = await PostAsync(client, "/api/v1/onboarding/context", new { token }, csrf))
            {
                Assert.Equal(HttpStatusCode.Conflict, usedContext.StatusCode);
                Assert.Equal("used-onboarding-token", await ReadProblemTypeAsync(usedContext));
            }

            await AssertSingleCompletedGraphAsync(factory, tenantId);

            using (var login = await PostAsync(client, "/api/v1/auth/login",
                       new { cpf = successful.Cpf, password = successful.Password }, csrf))
            {
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("activeTenant").ValueKind);
                var membership = Assert.Single(body.RootElement.GetProperty("memberships").EnumerateArray());
                Assert.Equal(tenantId, membership.GetProperty("tenantId").GetGuid());
            }

            csrf = await GetCsrfAsync(client);
            using (var selection = await PutAsync(client, "/api/v1/auth/active-tenant", new { tenantId }, csrf))
            {
                Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
                using var body = JsonDocument.Parse(await selection.Content.ReadAsStringAsync());
                Assert.Equal(tenantId, body.RootElement.GetProperty("activeTenant").GetProperty("id").GetGuid());
                Assert.Equal(successful.CompanyName, body.RootElement.GetProperty("activeTenant").GetProperty("name").GetString());
            }

            using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
            csrf = await GetCsrfAsync(anonymousClient);
            var expired = TestIdentity.Create();
            var expiredTenant = await CreateAsync(anonymousClient, csrf, expired);
            createdTenants.Add(expiredTenant);
            var expiredToken = factory.Sender.TakeToken(expired.Email);
            await ExpireTokenAsync(factory, expiredTenant);
            using (var response = await PostAsync(anonymousClient, "/api/v1/onboarding/context",
                       new { token = expiredToken }, csrf))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("expired-onboarding-token", await ReadProblemTypeAsync(response));
            }
            await AssertPendingGraphAsync(factory, expiredTenant);

            var revoked = TestIdentity.Create();
            var revokedTenant = await CreateAsync(anonymousClient, csrf, revoked);
            createdTenants.Add(revokedTenant);
            var revokedToken = factory.Sender.TakeToken(revoked.Email);
            await RevokeTokenAsync(factory, revokedTenant);
            using (var response = await PostAsync(anonymousClient, "/api/v1/onboarding/context",
                       new { token = revokedToken }, csrf))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("revoked-onboarding-token", await ReadProblemTypeAsync(response));
            }
            await AssertPendingGraphAsync(factory, revokedTenant);

            var rollback = TestIdentity.Create();
            factory.Sender.FailFor(rollback.Email);
            using (var failed = await PostAsync(anonymousClient, "/api/v1/commercial-onboardings/simulated-payment",
                       rollback.CommercialRequest, csrf))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            await AssertTenantDoesNotExistAsync(factory, rollback.Cnpj);
        }
        finally
        {
            await CleanupAsync(factory, createdTenants);
        }
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, object payload, string csrf)
        => await SendAsync(client, HttpMethod.Post, path, payload, csrf);

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string path, object payload, string csrf)
        => await SendAsync(client, HttpMethod.Put, path, payload, csrf);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object payload, string csrf)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string csrf, TestIdentity identity)
    {
        using var response = await PostAsync(client, "/api/v1/commercial-onboardings/simulated-payment",
            identity.CommercialRequest, csrf);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
        using var body = JsonDocument.Parse(responseBody);
        return body.RootElement.GetProperty("tenantId").GetGuid();
    }

    private static async Task<string?> ReadProblemTypeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("type").GetString()?.Split('/').Last();
    }

    private static async Task ExpireTokenAsync(RealOnboardingFactory factory, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        var membershipId = await db.TenantMemberships.Where(item => item.TenantId == tenantId)
            .Select(item => item.Id).SingleAsync();
        await db.AccountActionTokens.Where(item => item.TenantMembershipId == membershipId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ExpiresAt, item => item.CreatedAt.AddSeconds(1)));
    }

    private static async Task RevokeTokenAsync(RealOnboardingFactory factory, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        var membershipId = await db.TenantMemberships.Where(item => item.TenantId == tenantId)
            .Select(item => item.Id).SingleAsync();
        var token = await db.AccountActionTokens.SingleAsync(item => item.TenantMembershipId == membershipId);
        token.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task AssertSingleCompletedGraphAsync(RealOnboardingFactory factory, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(item => item.Id == tenantId));
        Assert.Equal(1, await db.TenantMemberships.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(1, await db.ClinicUnits.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(1, await db.ClinicUnitMemberships.CountAsync(item => item.TenantId == tenantId));
    }

    private static async Task AssertPendingGraphAsync(RealOnboardingFactory factory, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(item => item.Id == tenantId));
        Assert.Equal(0, await db.ClinicUnits.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(0, await db.ClinicUnitMemberships.CountAsync(item => item.TenantId == tenantId));
    }

    private static async Task AssertTenantDoesNotExistAsync(RealOnboardingFactory factory, string cnpj)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        Assert.False(await db.Tenants.AnyAsync(item => item.Cnpj == cnpj));
    }

    private static async Task CleanupAsync(RealOnboardingFactory factory, List<Guid> tenantIds)
    {
        if (tenantIds.Count == 0) return;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        var memberships = await db.TenantMemberships.Where(item => tenantIds.Contains(item.TenantId)).ToListAsync();
        var userIds = memberships.Select(item => item.UserId).Distinct().ToArray();
        var membershipIds = memberships.Select(item => item.Id).ToArray();
        db.ClinicUnitMemberships.RemoveRange(db.ClinicUnitMemberships.Where(item => tenantIds.Contains(item.TenantId)));
        db.ClinicUnits.RemoveRange(db.ClinicUnits.Where(item => tenantIds.Contains(item.TenantId)));
        db.AccountActionTokens.RemoveRange(db.AccountActionTokens.Where(item => membershipIds.Contains(item.TenantMembershipId!.Value)));
        db.UserSessions.RemoveRange(db.UserSessions.Where(item => userIds.Contains(item.UserId)));
        db.TenantMemberships.RemoveRange(memberships);
        db.Tenants.RemoveRange(db.Tenants.Where(item => tenantIds.Contains(item.Id)));
        db.UserCredentials.RemoveRange(db.UserCredentials.Where(item => userIds.Contains(item.UserId)));
        db.Users.RemoveRange(db.Users.Where(item => userIds.Contains(item.Id)));
        await db.SaveChangesAsync();
    }

    private static async Task CleanupStrandedTestDataAsync(RealOnboardingFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrothersCareDbContext>();
        var testUserIds = await db.Users
            .Where(item => item.NormalizedEmail.StartsWith("ONBOARDING-") && item.NormalizedEmail.EndsWith("@EXAMPLE.TEST"))
            .Select(item => item.Id)
            .ToArrayAsync();
        if (testUserIds.Length == 0) return;
        var tenantIds = await db.TenantMemberships.Where(item => testUserIds.Contains(item.UserId))
            .Select(item => item.TenantId).Distinct().ToListAsync();
        await CleanupAsync(factory, tenantIds);
    }

    private sealed class RealOnboardingFactory(string connectionString) : WebApplicationFactory<Program>, IAsyncDisposable
    {
        public CapturingCommercialOnboardingSender Sender { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(FindApiContentRoot());
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BrothersCare"] = connectionString,
                    ["CommercialOnboarding:AllowSimulatedPayment"] = "true",
                    ["Email:Smtp:Enabled"] = "false",
                    ["Authentication:CookieSecure"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICommercialOnboardingSender>();
                services.AddSingleton<ICommercialOnboardingSender>(Sender);
            });
        }

        private static string FindApiContentRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "BrothersCare.Api");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the BrothersCare.Api content root.");
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class CapturingCommercialOnboardingSender : ICommercialOnboardingSender
    {
        private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failures = new(StringComparer.OrdinalIgnoreCase);

        public Task SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken)
        {
            if (_failures.Contains(invitation.Email)) throw new InvalidOperationException("Simulated delivery failure.");
            _tokens[invitation.Email] = invitation.Token;
            return Task.CompletedTask;
        }

        public void FailFor(string email) => _failures.Add(email);
        public string TakeToken(string email) => _tokens.Remove(email, out var token) ? token : throw new InvalidOperationException("Invitation not captured.");
    }

    private sealed record TestIdentity(string CompanyName, string Cnpj, string Email, string Cpf, string Password)
    {
        public object CommercialRequest => new
        {
            companyName = CompanyName,
            cnpj = Cnpj,
            responsibleName = "Responsável de Teste",
            responsibleEmail = Email,
        };

        public object CompletionRequest(string token) => new
        {
            token,
            administrator = new { displayName = "Administradora de Teste", cpf = Cpf, password = Password, passwordConfirmation = Password },
            company = new { legalName = $"{CompanyName} Ltda.", tradeName = CompanyName, commercialEmail = Email, phone = "11987654321" },
            clinicUnit = new { name = "Unidade Matriz", postalCode = "01310100", street = "Avenida Paulista", number = "1000", complement = (string?)null, district = "Bela Vista", city = "São Paulo", state = "SP", phone = "1133334444" },
        };

        public static TestIdentity Create()
        {
            var digits = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(CultureInfo.InvariantCulture);
            return new($"Clínica Integração {digits[^6..]}", BuildCnpj(digits[..8] + "0001"), $"onboarding-{digits}@example.test", BuildCpf(digits), "SecurePassword123");
        }

        private static string BuildCpf(string seed)
        {
            var digits = seed[..9].Select(character => character - '0').ToList();
            digits.Add(CalculateDigit(digits, 10));
            digits.Add(CalculateDigit(digits, 11));
            return string.Concat(digits);
        }

        private static string BuildCnpj(string seed)
        {
            var digits = seed.Select(character => character - '0').ToList();
            int CalculateCnpjDigit(int[] weights)
            {
                var remainder = digits.Select((value, index) => value * weights[index]).Sum() % 11;
                return remainder < 2 ? 0 : 11 - remainder;
            }
            digits.Add(CalculateCnpjDigit([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
            digits.Add(CalculateCnpjDigit([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
            return string.Concat(digits);
        }

        private static int CalculateDigit(IReadOnlyList<int> digits, int weight)
        {
            var sum = digits.Select((value, index) => value * (weight - index)).Sum();
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
    }
}

internal sealed class RealDatabaseFactAttribute : FactAttribute
{
    public RealDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONBOARDING_TEST_CONNECTION_STRING")))
        {
            Skip = "Set ONBOARDING_TEST_CONNECTION_STRING to run the real SQL Server onboarding validation.";
        }
    }
}
