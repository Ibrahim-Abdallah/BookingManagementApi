using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingManagementApi.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_success()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
