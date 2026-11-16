using System.Text.Json;
using BrothersCare.Api.Care;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BrothersCare.Api.IntegrationTests;

public sealed class HealthChecksTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public HealthChecksTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        }).CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointReturnsSuccess(string endpoint)
    {
        using var response = await _client.GetAsync(endpoint);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CsrfCookieSupportsSecureCrossSiteFrontend()
    {
        using var response = await _client.GetAsync("/api/v1/auth/csrf-token");

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SchedulingOptionsRequiresAnAuthenticatedSession()
    {
        using var response = await _client.GetAsync("/api/v1/appointments/options");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/appointments?from=2026-11-18T00:00:00Z&to=2026-11-19T23:59:59Z")]
    [InlineData("/api/v1/appointments/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public async Task AppointmentQueryRoutesRequireAnAuthenticatedSession(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAppointmentRouteRequiresAnAuthenticatedSessionAfterCsrfValidation()
    {
        using var csrfResponse = await _client.GetAsync("/api/v1/auth/csrf-token");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfBody = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        var csrfToken = csrfBody.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("CSRF response did not contain a token.");
        var csrfCookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/appointments")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                patientId = Guid.NewGuid(),
                clinicUnitId = Guid.NewGuid(),
                startsAt = "2026-11-18T09:00:00-03:00",
                endsAt = "2026-11-18T10:00:00-03:00",
                notes = "Retorno de acompanhamento",
            }),
        };
        request.Headers.Add("Cookie", csrfCookie);
        request.Headers.Add("X-CSRF-Token", csrfToken);

        using var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void AppointmentRequestRejectsDateTimesWithoutAnOffset()
    {
        const string request = """
            {
              "patientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
              "clinicUnitId": "4fa85f64-5717-4562-b3fc-2c963f66afa6",
              "startsAt": "2026-11-18T09:00:00",
              "endsAt": "2026-11-18T10:00:00"
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateAppointmentRequest>(request, SerializerOptions));
    }

    [Fact]
    public void AppointmentRequestPreservesExplicitDateTimeOffsets()
    {
        const string request = """
            {
              "patientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
              "clinicUnitId": "4fa85f64-5717-4562-b3fc-2c963f66afa6",
              "startsAt": "2026-11-18T09:00:00-03:00",
              "endsAt": "2026-11-18T10:00:00Z"
            }
            """;

        var appointmentRequest = JsonSerializer.Deserialize<CreateAppointmentRequest>(
                request, SerializerOptions);

        Assert.NotNull(appointmentRequest);
        Assert.Equal(TimeSpan.FromHours(-3), appointmentRequest.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointmentRequest.EndsAt.Offset);
    }
}
