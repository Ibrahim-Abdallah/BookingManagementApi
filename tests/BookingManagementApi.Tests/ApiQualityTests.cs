using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingManagementApi.Contracts.Auth;
using BookingManagementApi.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingManagementApi.Tests;

public sealed class ApiQualityTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;

    public ApiQualityTests(AuthenticationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OpenApi_exposes_bearer_scheme_and_important_operation_metadata()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/auth/register", out _));
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
        Assert.True(paths.TryGetProperty("/api/reservation-holds", out _));
        Assert.True(paths.TryGetProperty("/api/reservations/{id}/confirm", out _));
        Assert.True(paths.TryGetProperty("/api/reservations/{id}/reschedule", out _));
        Assert.True(paths.TryGetProperty("/api/admin/reports/reservations-summary", out _));
        Assert.Equal("Search available booking slots", root.GetProperty("paths")
            .GetProperty("/api/availability").GetProperty("get").GetProperty("summary").GetString());
        Assert.Contains("not guaranteed", root.GetProperty("paths")
            .GetProperty("/api/availability").GetProperty("get").GetProperty("description").GetString());
        Assert.Contains("concurrency-safe", root.GetProperty("paths")
            .GetProperty("/api/reservation-holds").GetProperty("post").GetProperty("description").GetString());
        Assert.Contains("transactional conflict checking", paths.GetProperty("/api/reservations/{id}/reschedule")
            .GetProperty("post").GetProperty("description").GetString());
        Assert.Contains("Held and Expired reservations are excluded", paths
            .GetProperty("/api/admin/reports/reservations-summary").GetProperty("get")
            .GetProperty("description").GetString());
    }

    [Fact]
    public async Task Duplicate_registration_returns_conflict_problem_details()
    {
        using var client = _factory.CreateClient();
        var request = new RegisterRequest("Ada", "Lovelace", $"ada-{Guid.NewGuid()}@example.test", "StrongPassword123!", "StrongPassword123!");
        (await client.PostAsJsonAsync("/api/auth/register", request)).EnsureSuccessStatusCode();

        using var response = await client.PostAsJsonAsync("/api/auth/register", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(409, problem?.Status);
        Assert.Equal("Account conflict.", problem?.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem?.Detail));
    }

    [Fact]
    public async Task Validation_error_remains_validation_problem_details()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("", "", "bad", "short", "different"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unhandled_exception_returns_safe_problem_details()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.AddScoped<IAuthService, ThrowingAuthService>();
        }));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Ada", "Lovelace", "ada@example.test", "StrongPassword123!", "StrongPassword123!"));
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("An unexpected error occurred.", problem?.Title);
        Assert.Equal("An unexpected server error occurred.", problem?.Detail);
        Assert.DoesNotContain(ThrowingAuthService.SecretMessage, body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingAuthService : IAuthService
    {
        public const string SecretMessage = "database-internals-must-not-leak";
        public Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);
        public Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AuthResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
