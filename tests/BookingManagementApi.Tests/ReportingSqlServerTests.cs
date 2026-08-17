using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reports;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookingManagementApi.Tests;

public sealed class ReportingSqlServerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-17T12:00:00Z");
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
    public async Task Report_requires_admin()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/reports/reservations-summary")).StatusCode);
        using var customer = Client(AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/reports/reservations-summary")).StatusCode);
        using var admin = Client(AppRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/reports/reservations-summary")).StatusCode);
    }

    [Fact]
    public async Task Report_counts_only_operational_statuses_and_returns_empty_shape()
    {
        using var admin = Client(AppRoles.Admin);
        var empty = await admin.GetFromJsonAsync<ReservationSummaryResponse>("/api/admin/reports/reservations-summary");
        Assert.NotNull(empty);
        Assert.Equal(0, empty.TotalReservations);
        Assert.Empty(empty.TopServices);
        Assert.Empty(empty.TopResources);

        var graph = await SeedCatalogAsync(1, 1);
        await AddReservationsAsync(Enum.GetValues<ReservationStatus>()
            .Select((status, i) => NewReservation(graph, status, Now.AddDays(i), $"STATUS-{status}"))
            .ToArray());
        var report = await admin.GetFromJsonAsync<ReservationSummaryResponse>("/api/admin/reports/reservations-summary");

        Assert.NotNull(report);
        Assert.Equal(4, report.TotalReservations);
        Assert.Equal(1, report.Confirmed);
        Assert.Equal(1, report.Completed);
        Assert.Equal(1, report.Cancelled);
        Assert.Equal(1, report.NoShow);
        Assert.Equal(4, Assert.Single(report.TopServices).ReservationCount);
        Assert.Equal(4, Assert.Single(report.TopResources).ReservationCount);
    }

    [Fact]
    public async Task Report_date_boundaries_are_inclusive_and_invalid_range_is_bad_request()
    {
        var graph = await SeedCatalogAsync(1, 1);
        var from = Now.AddDays(-1);
        var to = Now.AddDays(1);
        var starts = new[] { from.AddTicks(-1), from, Now, to, to.AddTicks(1) };
        await AddReservationsAsync(starts.Select((start, i) => NewReservation(graph, ReservationStatus.Confirmed, start, $"DATE-{i}")).ToArray());
        using var admin = Client(AppRoles.Admin);

        var url = $"/api/admin/reports/reservations-summary?fromDate={Uri.EscapeDataString(from.ToString("O"))}&toDate={Uri.EscapeDataString(to.ToString("O"))}";
        var report = await admin.GetFromJsonAsync<ReservationSummaryResponse>(url);
        Assert.Equal(3, report!.TotalReservations);

        var invalid = $"/api/admin/reports/reservations-summary?fromDate={Uri.EscapeDataString(to.ToString("O"))}&toDate={Uri.EscapeDataString(from.ToString("O"))}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task Report_returns_deterministic_top_five_services_and_resources()
    {
        var graph = await SeedCatalogAsync(7, 7);
        var reservations = new List<Reservation>();
        for (var i = 0; i < 7; i++)
        {
            var count = i < 2 ? 7 : 7 - i;
            for (var j = 0; j < count; j++)
                reservations.Add(NewReservation(graph with { ServiceId = graph.ServiceIds[i], ResourceId = graph.ResourceIds[i] }, ReservationStatus.Completed, Now.AddMinutes(j), $"TOP-{i}-{j}"));
            reservations.Add(NewReservation(graph with { ServiceId = graph.ServiceIds[i], ResourceId = graph.ResourceIds[i] }, ReservationStatus.Held, Now, $"HELD-{i}"));
            reservations.Add(NewReservation(graph with { ServiceId = graph.ServiceIds[i], ResourceId = graph.ResourceIds[i] }, ReservationStatus.Expired, Now, $"EXPIRED-{i}"));
        }
        await AddReservationsAsync(reservations.ToArray());
        using var admin = Client(AppRoles.Admin);
        var report = await admin.GetFromJsonAsync<ReservationSummaryResponse>("/api/admin/reports/reservations-summary");

        Assert.Equal(5, report!.TopServices.Count);
        Assert.Equal(5, report.TopResources.Count);
        Assert.Equal(new[] { "Service A", "Service B" }, report.TopServices.Take(2).Select(x => x.ServiceName));
        Assert.Equal(new[] { "Resource A", "Resource B" }, report.TopResources.Take(2).Select(x => x.ResourceName));
        Assert.Equal(new[] { 7, 7, 5, 4, 3 }, report.TopServices.Select(x => x.ReservationCount));
        Assert.Equal(new[] { 7, 7, 5, 4, 3 }, report.TopResources.Select(x => x.ReservationCount));
    }

    private async Task<Graph> SeedCatalogAsync(int serviceCount, int resourceCount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Id = Guid.NewGuid(), FirstName = "Report", LastName = "User", Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "x", Role = AppRoles.Customer, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var services = Enumerable.Range(0, serviceCount).Select(i => new Service { Id = Guid.NewGuid(), Name = $"Service {(char)('A' + i)}", DurationMinutes = 30, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now }).ToArray();
        var resources = Enumerable.Range(0, resourceCount).Select(i => new Resource { Id = Guid.NewGuid(), Name = $"Resource {(char)('A' + i)}", IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now }).ToArray();
        db.Add(user); db.AddRange(services); db.AddRange(resources);
        await db.SaveChangesAsync();
        return new(user.Id, services[0].Id, resources[0].Id, services.Select(x => x.Id).ToArray(), resources.Select(x => x.Id).ToArray());
    }

    private async Task AddReservationsAsync(params Reservation[] reservations)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Reservations.AddRange(reservations);
        await db.SaveChangesAsync();
    }

    private static Reservation NewReservation(Graph graph, ReservationStatus status, DateTimeOffset startsAt, string reference) => new()
    {
        Id = Guid.NewGuid(), ReferenceNumber = reference, UserId = graph.UserId, ServiceId = graph.ServiceId, ResourceId = graph.ResourceId,
        StartsAtUtc = startsAt, EndsAtUtc = startsAt.AddMinutes(30), Status = status,
        HoldExpiresAtUtc = status is ReservationStatus.Held or ReservationStatus.Expired ? Now.AddHours(1) : null,
        CreatedAtUtc = Now, UpdatedAtUtc = Now, ServiceNameSnapshot = "Snapshot", ResourceNameSnapshot = "Snapshot", ServiceDurationMinutesSnapshot = 30
    };

    private HttpClient Client(string role)
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "BookingManagementApi.Tests", "BookingManagementApi.Tests.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim(JwtRegisteredClaimNames.Email, "report@example.com"), new Claim(ClaimTypes.Role, role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Graph(Guid UserId, Guid ServiceId, Guid ResourceId, Guid[] ServiceIds, Guid[] ResourceIds);
}
