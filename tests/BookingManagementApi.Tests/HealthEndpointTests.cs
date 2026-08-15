namespace BookingManagementApi.Tests;

public sealed class HealthEndpointTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(AuthenticationApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_success()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
