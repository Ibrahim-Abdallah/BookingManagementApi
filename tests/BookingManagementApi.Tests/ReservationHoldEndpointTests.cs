using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookingManagementApi.Tests;

public sealed class ReservationHoldEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T08:30:00Z");
    private AuthenticationApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new AuthenticationApiFactory { Clock = new FixedClock(Now) };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Anonymous_is_rejected_and_admin_is_forbidden()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/reservation-holds",
            new CreateReservationHoldRequest(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1)))).StatusCode);
        using var admin = Client(_factory, Guid.NewGuid(), AppRoles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/reservation-holds",
            new CreateReservationHoldRequest(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1)))).StatusCode);
    }

    [Fact]
    public async Task Valid_hold_derives_ownership_times_status_snapshots_and_reference()
    {
        var graph = await SeedAsync();
        using var client = Client(_factory, graph.UserId, AppRoles.Customer);
        var response = await client.PostAsJsonAsync("/api/reservation-holds",
            new { graph.ServiceId, graph.ResourceId, StartsAtUtc = "2026-08-20T09:00:00Z",
                UserId = Guid.NewGuid(), EndsAtUtc = "2099-01-01T00:00:00Z", Status = "Confirmed" });
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ReservationHoldResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Held", body.Status);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Held", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T09:30:00Z"), body.EndsAtUtc);
        Assert.Equal(Now.AddMinutes(5), body.HoldExpiresAtUtc);
        Assert.StartsWith("BKG-20260820-", body.ReferenceNumber);
        await using var scope = _factory.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.FindAsync(body.ReservationId);
        Assert.NotNull(saved);
        Assert.Equal(graph.UserId, saved.UserId);
        Assert.Equal("Consultation", saved.ServiceNameSnapshot);
        Assert.Equal("Room A", saved.ResourceNameSnapshot);
        Assert.Equal(30, saved.ServiceDurationMinutesSnapshot);
    }

    [Fact]
    public async Task Confirmed_and_live_holds_conflict_but_expired_and_adjacent_holds_do_not()
    {
        var graph = await SeedAsync();
        await AddReservationAsync(graph, "CONF", ReservationStatus.Confirmed, "2026-08-20T09:00:00Z", "2026-08-20T09:30:00Z", null);
        using var client = Client(_factory, graph.UserId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, graph, "2026-08-20T09:00:00Z")).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reservations.RemoveRange(db.Reservations);
            await db.SaveChangesAsync();
        }
        await AddReservationAsync(graph, "OLD", ReservationStatus.Held, "2026-08-20T09:00:00Z", "2026-08-20T09:30:00Z", Now);
        Assert.Equal(HttpStatusCode.Created, (await Post(client, graph, "2026-08-20T09:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(client, graph, "2026-08-20T09:30:00Z")).StatusCode);
    }

    [Fact]
    public async Task Invalid_catalog_schedule_window_alignment_and_block_are_rejected()
    {
        var graph = await SeedAsync();
        using var client = Client(_factory, graph.UserId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, graph with { ServiceId = Guid.NewGuid() }, "2026-08-20T09:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, graph, "2026-08-20T09:01:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, graph, "2026-08-20T12:00:00Z")).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BlockedPeriods.Add(new() { Id = Guid.NewGuid(), ResourceId = graph.ResourceId,
            StartsAtUtc = DateTimeOffset.Parse("2026-08-20T10:00:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-08-20T10:30:00Z"), CreatedAtUtc = Now });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, graph, "2026-08-20T10:00:00Z")).StatusCode);
    }

    private async Task<Graph> SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Customer", Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "x", Role = AppRoles.Customer, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var service = new Service { Id = Guid.NewGuid(), Name = "Consultation", DurationMinutes = 30, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var resource = new Resource { Id = Guid.NewGuid(), Name = "Room A", IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        db.AddRange(user, service, resource);
        db.ResourceServices.Add(new() { ResourceId = resource.Id, ServiceId = service.Id });
        db.AvailabilityRules.Add(new() { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(11, 0), IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        await db.SaveChangesAsync();
        return new(user.Id, service.Id, resource.Id);
    }

    private async Task AddReservationAsync(Graph graph, string reference, ReservationStatus status, string start, string end, DateTimeOffset? expiry)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Reservations.Add(new() { Id = Guid.NewGuid(), ReferenceNumber = reference, UserId = graph.UserId, ServiceId = graph.ServiceId, ResourceId = graph.ResourceId, StartsAtUtc = DateTimeOffset.Parse(start), EndsAtUtc = DateTimeOffset.Parse(end), Status = status, HoldExpiresAtUtc = expiry, CreatedAtUtc = Now, UpdatedAtUtc = Now, ServiceNameSnapshot = "Consultation", ResourceNameSnapshot = "Room A", ServiceDurationMinutesSnapshot = 30 });
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, Graph graph, string start) =>
        client.PostAsJsonAsync("/api/reservation-holds", new CreateReservationHoldRequest(graph.ServiceId, graph.ResourceId, DateTimeOffset.Parse(start)));

    internal static HttpClient Client(AuthenticationApiFactory factory, Guid userId, string role)
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("BookingManagementApi.Tests", "BookingManagementApi.Tests.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(JwtRegisteredClaimNames.Email, "hold@example.com"), new Claim(ClaimTypes.Role, role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Graph(Guid UserId, Guid ServiceId, Guid ResourceId);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
