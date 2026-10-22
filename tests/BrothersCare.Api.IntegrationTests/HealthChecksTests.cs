using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BrothersCare.Api.IntegrationTests;

public sealed class HealthChecksTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthChecksTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing")).CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointReturnsSuccess(string endpoint)
    {
        using var response = await _client.GetAsync(endpoint);

        response.EnsureSuccessStatusCode();
    }
}
