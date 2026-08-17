using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingManagementApi.Tests;

public sealed class ReservationLifecycleEndpointTests : IAsyncLifetime
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
    public async Task Authorization_and_ownership_are_enforced_with_foreign_rows_hidden()
    {
        var graph = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/reservations")).StatusCode);
        using var admin = Client(graph.AdminId, AppRoles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/reservations")).StatusCode);
        using var owner = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/reservations")).StatusCode);

        var foreign = await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Held, "FOREIGN", "2026-08-20T09:00:00Z", Now.AddMinutes(5));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/reservations/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/reservations/{foreign.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/reservations/{foreign.Id}/cancel", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/reservations/{foreign.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/admin/reservations/{foreign.Id}/status", new { status = "Completed" })).StatusCode);
    }

    [Fact]
    public async Task Customer_list_details_pagination_filters_order_and_snapshots_work()
    {
        var graph = await SeedAsync();
        var older = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "OLDER", "2026-08-20T09:00:00Z", Now.AddMinutes(5));
        var laterLow = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "LATER-LOW", "2026-08-20T10:00:00Z", null, Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var laterHigh = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "LATER-HIGH", "2026-08-20T10:00:00Z", null, Guid.Parse("f0000000-0000-0000-0000-000000000000"));
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Confirmed, "OTHER", "2026-08-20T11:00:00Z", null);
        using var client = Client(graph.OwnerId, AppRoles.Customer);

        var list = await client.GetFromJsonAsync<PagedResponse<ReservationResponse>>("/api/reservations?pageNumber=1&pageSize=2");
        Assert.NotNull(list); Assert.Equal(3, list.TotalCount); Assert.Equal(2, list.Items.Count);
        Assert.Equal([laterHigh.Id, laterLow.Id], list.Items.Select(x => x.ReservationId));
        var filtered = await client.GetFromJsonAsync<PagedResponse<ReservationResponse>>("/api/reservations?status=Held&fromDate=2026-08-20T08:59:00Z&toDate=2026-08-20T09:01:00Z");
        Assert.NotNull(filtered); Assert.Single(filtered.Items); Assert.Equal(older.Id, filtered.Items[0].ReservationId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reservations?fromDate=2026-08-21T00:00:00Z&toDate=2026-08-20T00:00:00Z")).StatusCode);
        var detail = await client.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{older.Id}");
        Assert.NotNull(detail); Assert.Equal("Historic Service", detail.ServiceName); Assert.Equal("Historic Resource", detail.ResourceName);
    }

    [Fact]
    public async Task Confirm_is_time_based_clears_expiry_and_is_idempotent()
    {
        var graph = await SeedAsync();
        var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "CONFIRM", "2026-08-20T09:00:00Z", Now.AddMinutes(5));
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        var first = await client.PostAsync($"/api/reservations/{row.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(body); Assert.Equal("Confirmed", body.Status); Assert.Equal(Now, body.ConfirmedAtUtc); Assert.Null(body.HoldExpiresAtUtc);
        var second = await client.PostAsync($"/api/reservations/{row.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(Now, (await second.Content.ReadFromJsonAsync<ReservationResponse>())!.ConfirmedAtUtc);
    }

    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Expired)]
    public async Task Confirm_rejects_terminal_statuses(ReservationStatus status)
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, status, $"CONFIRM-{status}", "2026-08-20T09:00:00Z", null);
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/reservations/{row.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Confirm_rejects_logically_expired_hold()
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "EXPIRED-HOLD", "2026-08-20T09:00:00Z", Now);
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/reservations/{row.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Held_cancellation_trims_reason_clears_expiry_and_repeat_is_conflict()
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "CANCEL-HOLD", "2026-08-20T09:00:00Z", Now.AddMinutes(5));
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        var response = await client.PostAsJsonAsync($"/api/reservations/{row.Id}/cancel", new { reason = "  Schedule changed  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(body); Assert.Equal("Cancelled", body.Status); Assert.Equal("Schedule changed", body.CancellationReason); Assert.Equal(Now, body.CancelledAtUtc); Assert.Null(body.HoldExpiresAtUtc);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{row.Id}/cancel", new { reason = "again" })).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope(); var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.FindAsync(row.Id);
        Assert.Equal("Schedule changed", saved!.CancellationReason); Assert.Equal(Now, saved.CancelledAtUtc);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{Guid.NewGuid()}/cancel", new { reason = new string('x', 501) })).StatusCode);
    }

    [Fact]
    public async Task Confirmed_cancellation_allows_exact_cutoff_and_rejects_after_cutoff()
    {
        var graph = await SeedAsync();
        var boundary = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "BOUNDARY", Now.AddMinutes(60).ToString("O"), null);
        var late = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "LATE", Now.AddMinutes(59).ToString("O"), null);
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/reservations/{boundary.Id}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{late.Id}/cancel", new { })).StatusCode);
    }

    [Theory]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.NoShow)]
    [InlineData(ReservationStatus.Expired)]
    public async Task Cancellation_rejects_expired_or_terminal_reservations(ReservationStatus status)
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, status, $"CANCEL-{status}", "2026-08-20T09:00:00Z", null);
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{row.Id}/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Cancellation_rejects_logically_expired_hold()
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "CANCEL-EXPIRED", "2026-08-20T09:00:00Z", Now);
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{row.Id}/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Reschedule_preserves_identity_snapshots_confirmation_and_uses_snapshot_duration()
    {
        var graph = await SeedAsync(); var confirmedAt = Now.AddDays(-1);
        var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "KEEP-REFERENCE", "2026-08-20T09:00:00Z", null, confirmedAt: confirmedAt);
        await MutateAsync(db => { db.Services.Single(x => x.Id == graph.ServiceId).DurationMinutes = 60; return Task.CompletedTask; });
        using var client = Client(graph.OwnerId, AppRoles.Customer);
        var response = await client.PostAsJsonAsync($"/api/reservations/{row.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<ReservationResponse>(); Assert.NotNull(body);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T10:00:00Z"), body.StartsAtUtc); Assert.Equal(DateTimeOffset.Parse("2026-08-20T10:30:00Z"), body.EndsAtUtc);
        Assert.Equal("Confirmed", body.Status); Assert.Equal(row.ReferenceNumber, body.ReferenceNumber); Assert.Equal(graph.ResourceId, body.ResourceId); Assert.Equal(graph.ServiceId, body.ServiceId);
        Assert.Equal(confirmedAt, body.ConfirmedAtUtc); Assert.Equal("Historic Service", body.ServiceName); Assert.Equal("Historic Resource", body.ResourceName);
    }

    [Fact]
    public async Task Reschedule_rejects_invalid_time_schedule_and_catalog_states()
    {
        var graph = await SeedAsync(); using var client = Client(graph.OwnerId, AppRoles.Customer);
        async Task<HttpStatusCode> Attempt(string reference, string start, Func<AppDbContext, Task>? mutate = null)
        {
            var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, reference, "2026-08-20T09:00:00Z", null);
            if (mutate is not null) await MutateAsync(mutate);
            return (await client.PostAsJsonAsync($"/api/reservations/{row.Id}/reschedule", new { startsAtUtc = start })).StatusCode;
        }
        Assert.Equal(HttpStatusCode.BadRequest, await Attempt("SAME", "2026-08-20T09:00:00Z"));
        Assert.Equal(HttpStatusCode.BadRequest, await Attempt("OFFSET", "2026-08-20T12:00:00+02:00"));
        Assert.Equal(HttpStatusCode.BadRequest, await Attempt("MISALIGNED", "2026-08-20T10:01:00Z"));
        Assert.Equal(HttpStatusCode.BadRequest, await Attempt("OUTSIDE", "2026-08-20T14:00:00Z"));
        Assert.Equal(HttpStatusCode.BadRequest, await Attempt("INACTIVE-SERVICE", "2026-08-20T10:00:00Z", db => { db.Services.Single(x => x.Id == graph.ServiceId).IsActive = false; return Task.CompletedTask; }));
    }

    [Fact]
    public async Task Reschedule_rejects_block_inactive_resource_and_unsupported_assignment()
    {
        var graph = await SeedAsync(); using var client = Client(graph.OwnerId, AppRoles.Customer);
        var blocked = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "BLOCKED", "2026-08-20T09:00:00Z", null);
        await MutateAsync(db => { db.BlockedPeriods.Add(new() { Id = Guid.NewGuid(), ResourceId = graph.ResourceId, StartsAtUtc = DateTimeOffset.Parse("2026-08-20T10:00:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-08-20T10:30:00Z"), CreatedAtUtc = Now }); return Task.CompletedTask; });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{blocked.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        await MutateAsync(db => { db.BlockedPeriods.RemoveRange(db.BlockedPeriods); db.Resources.Single(x => x.Id == graph.ResourceId).IsActive = false; return Task.CompletedTask; });
        var inactive = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "INACTIVE-RESOURCE", "2026-08-20T09:30:00Z", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{inactive.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        await MutateAsync(db => { db.Resources.Single(x => x.Id == graph.ResourceId).IsActive = true; db.ResourceServices.RemoveRange(db.ResourceServices); return Task.CompletedTask; });
        var unsupported = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "UNSUPPORTED", "2026-08-20T11:00:00Z", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{unsupported.Id}/reschedule", new { startsAtUtc = "2026-08-20T12:00:00Z" })).StatusCode);
    }

    [Fact]
    public async Task Reschedule_enforces_window_status_conflicts_and_preserves_failed_row()
    {
        var graph = await SeedAsync(); using var client = Client(graph.OwnerId, AppRoles.Customer);
        var held = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Held, "NOT-CONFIRMED", "2026-08-20T09:00:00Z", Now.AddMinutes(5));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{held.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        var tooSoon = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "TOO-SOON", "2026-08-20T09:30:00Z", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{tooSoon.Id}/reschedule", new { startsAtUtc = "2026-08-16T08:45:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/reservations/{tooSoon.Id}/reschedule", new { startsAtUtc = "2026-11-20T10:00:00Z" })).StatusCode);

        var moving = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "MOVING", "2026-08-20T09:00:00Z", null);
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Confirmed, "BLOCKER", "2026-08-20T10:00:00Z", null);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{moving.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope(); var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync(x => x.Id == moving.Id);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T09:00:00Z"), saved.StartsAtUtc); Assert.Equal("MOVING", saved.ReferenceNumber); Assert.Equal(ReservationStatus.Confirmed, saved.Status);
    }

    [Fact]
    public async Task Expired_hold_and_adjacency_do_not_block_but_live_hold_does()
    {
        var graph = await SeedAsync(); using var client = Client(graph.OwnerId, AppRoles.Customer);
        var moving = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "MOVE-EXPIRED", "2026-08-20T09:00:00Z", null);
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Held, "EXPIRED-BLOCKER", "2026-08-20T10:00:00Z", Now);
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Confirmed, "ADJACENT", "2026-08-20T10:30:00Z", null);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/reservations/{moving.Id}/reschedule", new { startsAtUtc = "2026-08-20T10:00:00Z" })).StatusCode);
        var second = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "MOVE-LIVE", "2026-08-20T11:00:00Z", null);
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Held, "LIVE-BLOCKER", "2026-08-20T12:00:00Z", Now.AddMinutes(5));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/reservations/{second.Id}/reschedule", new { startsAtUtc = "2026-08-20T12:00:00Z" })).StatusCode);
    }

    [Fact]
    public async Task Admin_list_details_filters_pagination_and_date_validation_work()
    {
        var graph = await SeedAsync();
        var first = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, "ADMIN-FIRST", "2026-08-20T09:00:00Z", null);
        await AddReservationAsync(graph, graph.OtherId, ReservationStatus.Held, "ADMIN-SECOND", "2026-08-20T10:00:00Z", Now.AddMinutes(5));
        using var admin = Client(graph.AdminId, AppRoles.Admin);
        var page = await admin.GetFromJsonAsync<PagedResponse<AdminReservationResponse>>($"/api/admin/reservations?pageSize=1&pageNumber=1&status=Confirmed&resourceId={graph.ResourceId}&serviceId={graph.ServiceId}&customerEmail=OWNER@EXAMPLE.COM&fromDate=2026-08-20T08:00:00Z&toDate=2026-08-20T09:30:00Z");
        Assert.NotNull(page); Assert.Single(page.Items); Assert.Equal(first.Id, page.Items[0].ReservationId); Assert.Equal("owner@example.com", page.Items[0].CustomerEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/reservations?fromDate=2026-08-21T00:00:00Z&toDate=2026-08-20T00:00:00Z")).StatusCode);
        var detail = await admin.GetFromJsonAsync<AdminReservationResponse>($"/api/admin/reservations/{first.Id}"); Assert.NotNull(detail); Assert.Equal(graph.OwnerId, detail.CustomerId);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("NoShow")]
    public async Task Admin_can_apply_only_supported_confirmed_transitions(string target)
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Confirmed, $"ADMIN-{target}", "2026-08-20T09:00:00Z", null);
        using var admin = Client(graph.AdminId, AppRoles.Admin);
        var response = await admin.PatchAsJsonAsync($"/api/admin/reservations/{row.Id}/status", new { status = target });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(target, (await response.Content.ReadFromJsonAsync<AdminReservationResponse>())!.Status);
    }

    [Fact]
    public async Task Admin_rejects_unsupported_target_and_invalid_transition()
    {
        var graph = await SeedAsync(); var row = await AddReservationAsync(graph, graph.OwnerId, ReservationStatus.Cancelled, "ADMIN-INVALID", "2026-08-20T09:00:00Z", null);
        using var admin = Client(graph.AdminId, AppRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/admin/reservations/{row.Id}/status", new { status = "Confirmed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/reservations/{row.Id}/status", new { status = "Completed" })).StatusCode);
    }

    private async Task<Graph> SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = User("owner@example.com", AppRoles.Customer); var other = User("other@example.com", AppRoles.Customer); var admin = User("admin@example.com", AppRoles.Admin);
        var service = new Service { Id = Guid.NewGuid(), Name = "Current Service", DurationMinutes = 30, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var resource = new Resource { Id = Guid.NewGuid(), Name = "Current Resource", IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        db.AddRange(owner, other, admin, service, resource); db.ResourceServices.Add(new() { ResourceId = resource.Id, ServiceId = service.Id });
        db.AvailabilityRules.Add(new() { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(14, 0), IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        await db.SaveChangesAsync(); return new(owner.Id, other.Id, admin.Id, service.Id, resource.Id);
    }

    private async Task<Reservation> AddReservationAsync(Graph graph, Guid userId, ReservationStatus status, string reference, string start,
        DateTimeOffset? expiry, Guid? id = null, DateTimeOffset? confirmedAt = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var starts = DateTimeOffset.Parse(start); var row = new Reservation { Id = id ?? Guid.NewGuid(), ReferenceNumber = reference, UserId = userId,
            ResourceId = graph.ResourceId, ServiceId = graph.ServiceId, StartsAtUtc = starts, EndsAtUtc = starts.AddMinutes(30), Status = status,
            HoldExpiresAtUtc = expiry, CreatedAtUtc = Now, UpdatedAtUtc = Now, ConfirmedAtUtc = confirmedAt,
            ServiceNameSnapshot = "Historic Service", ResourceNameSnapshot = "Historic Resource", ServiceDurationMinutesSnapshot = 30 };
        db.Reservations.Add(row); await db.SaveChangesAsync(); return row;
    }

    private async Task MutateAsync(Func<AppDbContext, Task> mutation)
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await mutation(db); await db.SaveChangesAsync();
    }
    private User User(string email, string role) => new() { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User", Email = email,
        NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "x", Role = role, CreatedAtUtc = Now, UpdatedAtUtc = Now };
    private HttpClient Client(Guid id, string role) => ReservationHoldEndpointTests.Client(_factory, id, role);
    private sealed record Graph(Guid OwnerId, Guid OtherId, Guid AdminId, Guid ServiceId, Guid ResourceId);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
