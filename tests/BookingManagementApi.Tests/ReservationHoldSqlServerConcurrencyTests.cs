using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BookingManagementApi.Tests;

public sealed class ReservationHoldSqlServerConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T08:30:00Z");
    private SqlServerApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new SqlServerApiFactory(Now);
        await _factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DeleteDatabaseAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Same_resource_and_interval_produce_one_created_one_conflict_and_one_row()
    {
        var graph = await SeedAsync(1);
        using var first = Client(graph.UserIds[0]);
        using var second = Client(graph.UserIds[1]);
        var request = new CreateReservationHoldRequest(graph.ServiceId, graph.ResourceIds[0], DateTimeOffset.Parse("2026-08-20T09:00:00Z"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = new[] { SendAfterGate(first, request, gate.Task), SendAfterGate(second, request, gate.Task) };
        gate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = _factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(ReservationStatus.Held, rows[0].Status);
        Assert.True(rows[0].HoldExpiresAtUtc > Now);
    }

    [Fact]
    public async Task Different_resources_both_succeed_and_create_two_rows()
    {
        var graph = await SeedAsync(2);
        using var first = Client(graph.UserIds[0]);
        using var second = Client(graph.UserIds[1]);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = graph.ResourceIds.Select((resourceId, index) => SendAfterGate(index == 0 ? first : second,
            new(graph.ServiceId, resourceId, DateTimeOffset.Parse("2026-08-20T09:00:00Z")), gate.Task)).ToArray();
        gate.SetResult();
        var responses = await Task.WhenAll(calls);

        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        await using var scope = _factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(x => x.ResourceId).Distinct().Count());
    }

    [Fact]
    public async Task Conflict_rolls_back_without_partial_reservation()
    {
        var graph = await SeedAsync(1);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reservations.Add(NewReservation(graph, "EXISTING"));
            await db.SaveChangesAsync();
        }
        using var client = Client(graph.UserIds[1]);
        var response = await client.PostAsJsonAsync("/api/reservation-holds",
            new CreateReservationHoldRequest(graph.ServiceId, graph.ResourceIds[0], DateTimeOffset.Parse("2026-08-20T09:00:00Z")));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var rows = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("EXISTING", rows[0].ReferenceNumber);
    }

    [Fact]
    public async Task Concurrent_reschedules_to_same_interval_produce_one_success_and_leave_loser_unchanged()
    {
        var graph = await SeedAsync(1);
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reservations.AddRange(
                NewConfirmed(graph, firstId, graph.UserIds[0], "RESCHEDULE-A", "2026-08-20T09:00:00Z"),
                NewConfirmed(graph, secondId, graph.UserIds[1], "RESCHEDULE-B", "2026-08-20T09:30:00Z"));
            await db.SaveChangesAsync();
        }
        using var first = Client(graph.UserIds[0]); using var second = Client(graph.UserIds[1]);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        var calls = new[] { RescheduleAfterGate(first, firstId, target, gate.Task), RescheduleAfterGate(second, secondId, target, gate.Task) };
        gate.SetResult(); var responses = await Task.WhenAll(calls);
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));

        await using var verify = _factory.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().OrderBy(x => x.ReferenceNumber).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.Single(rows, x => x.StartsAtUtc == target);
        Assert.Contains(rows, x => x.ReferenceNumber == "RESCHEDULE-A" && x.StartsAtUtc == DateTimeOffset.Parse("2026-08-20T09:00:00Z") || x.ReferenceNumber == "RESCHEDULE-B" && x.StartsAtUtc == DateTimeOffset.Parse("2026-08-20T09:30:00Z"));
        Assert.False(rows[0].StartsAtUtc < rows[1].EndsAtUtc && rows[0].EndsAtUtc > rows[1].StartsAtUtc);
    }

    private async Task<Graph> SeedAsync(int resourceCount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = Enumerable.Range(0, 2).Select(i => new User { Id = Guid.NewGuid(), FirstName = "Customer", LastName = i.ToString(), Email = $"sql-{Guid.NewGuid():N}@example.com", NormalizedEmail = $"SQL-{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "x", Role = AppRoles.Customer, CreatedAtUtc = Now, UpdatedAtUtc = Now }).ToArray();
        var service = new Service { Id = Guid.NewGuid(), Name = "SQL Service", DurationMinutes = 30, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var resources = Enumerable.Range(0, resourceCount).Select(i => new Resource { Id = Guid.NewGuid(), Name = $"SQL Resource {i}", IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now }).ToArray();
        db.AddRange(users); db.Add(service); db.AddRange(resources);
        foreach (var resource in resources)
        {
            db.ResourceServices.Add(new() { ResourceId = resource.Id, ServiceId = service.Id });
            db.AvailabilityRules.Add(new() { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(11, 0), IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        }
        await db.SaveChangesAsync();
        return new(users.Select(x => x.Id).ToArray(), service.Id, resources.Select(x => x.Id).ToArray());
    }

    private Reservation NewReservation(Graph graph, string reference) => new() { Id = Guid.NewGuid(), ReferenceNumber = reference,
        UserId = graph.UserIds[0], ServiceId = graph.ServiceId, ResourceId = graph.ResourceIds[0],
        StartsAtUtc = DateTimeOffset.Parse("2026-08-20T09:00:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-08-20T09:30:00Z"),
        Status = ReservationStatus.Confirmed, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        ServiceNameSnapshot = "SQL Service", ResourceNameSnapshot = "SQL Resource 0", ServiceDurationMinutesSnapshot = 30 };

    private Reservation NewConfirmed(Graph graph, Guid id, Guid userId, string reference, string start) => new()
    {
        Id = id, ReferenceNumber = reference, UserId = userId, ServiceId = graph.ServiceId, ResourceId = graph.ResourceIds[0],
        StartsAtUtc = DateTimeOffset.Parse(start), EndsAtUtc = DateTimeOffset.Parse(start).AddMinutes(30), Status = ReservationStatus.Confirmed,
        CreatedAtUtc = Now, UpdatedAtUtc = Now, ConfirmedAtUtc = Now, ServiceNameSnapshot = "SQL Service",
        ResourceNameSnapshot = "SQL Resource 0", ServiceDurationMinutesSnapshot = 30
    };

    private HttpClient Client(Guid userId)
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("BookingManagementApi.Tests", "BookingManagementApi.Tests.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(JwtRegisteredClaimNames.Email, "sql@example.com"), new Claim(ClaimTypes.Role, AppRoles.Customer)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
    }

    private static async Task<HttpResponseMessage> SendAfterGate(HttpClient client, CreateReservationHoldRequest request, Task gate)
    { await gate; return await client.PostAsJsonAsync("/api/reservation-holds", request); }
    private static async Task<HttpResponseMessage> RescheduleAfterGate(HttpClient client, Guid id, DateTimeOffset start, Task gate)
    { await gate; return await client.PostAsJsonAsync($"/api/reservations/{id}/reschedule", new RescheduleReservationRequest(start)); }

    private sealed record Graph(Guid[] UserIds, Guid ServiceId, Guid[] ResourceIds);
}

internal sealed class SqlServerApiFactory(DateTimeOffset now) : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"BookingManagementApiPhase07_{Guid.NewGuid():N}";
    private string ConnectionString
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("BOOKING_TEST_SQLSERVER_CONNECTION");
            if (string.IsNullOrWhiteSpace(configured))
                return $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = _databaseName,
                TrustServerCertificate = true
            };
            return builder.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(x => x.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "BookingManagementApi.Tests", ["Jwt:Audience"] = "BookingManagementApi.Tests.Client",
            ["Jwt:Key"] = AuthenticationApiFactory.JwtKey, ["Jwt:AccessTokenExpirationMinutes"] = "15", ["Jwt:RefreshTokenExpirationDays"] = "7",
            ["Scheduling:BusinessTimeZoneId"] = "UTC", ["Scheduling:SlotIntervalMinutes"] = "15", ["Scheduling:HoldDurationMinutes"] = "5",
            ["Scheduling:MinimumAdvanceMinutes"] = "30", ["Scheduling:MaximumBookingHorizonDays"] = "90",
            ["Scheduling:MinimumCancellationNoticeMinutes"] = "60", ["Scheduling:HoldCleanupIntervalSeconds"] = "60"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(x => x.UseSqlServer(ConnectionString));
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedSqlClock(now));
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DeleteDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
    }

    private sealed class FixedSqlClock(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
}
