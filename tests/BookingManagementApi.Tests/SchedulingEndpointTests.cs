using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Scheduling;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using BookingManagementApi.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingManagementApi.Tests;

public sealed class SchedulingEndpointTests(AuthenticationApiFactory factory) : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory = factory;

    [Fact]
    public async Task Schedule_endpoints_require_admin()
    {
        var resource = await SeedResourceAsync();
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/admin/resources/{resource.Id}/availability-rules")).StatusCode);
        using var customer = await ClientForRoleAsync(AppRoles.Customer);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customer.GetAsync($"/api/admin/resources/{resource.Id}/availability-rules")).StatusCode);
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/admin/resources/{resource.Id}/availability-rules")).StatusCode);
    }

    [Fact]
    public async Task Availability_rules_support_multiple_adjacent_shifts_and_reject_overlaps()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var firstResource = await SeedResourceAsync();
        var secondResource = await SeedResourceAsync();
        var firstUrl = $"/api/admin/resources/{firstResource.Id}/availability-rules";

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(firstUrl,
            new CreateAvailabilityRuleRequest(DayOfWeek.Monday, new(9, 0), new(9, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(firstUrl,
            new CreateAvailabilityRuleRequest((DayOfWeek)99, new(9, 0), new(10, 0)))).StatusCode);

        var first = await CreateRuleAsync(admin, firstResource.Id, DayOfWeek.Monday, new(9, 0), new(12, 0));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(firstUrl,
            new CreateAvailabilityRuleRequest(DayOfWeek.Monday, new(10, 0), new(11, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(firstUrl,
            new CreateAvailabilityRuleRequest(DayOfWeek.Monday, new(11, 0), new(13, 0)))).StatusCode);
        var adjacent = await CreateRuleAsync(admin, firstResource.Id, DayOfWeek.Monday, new(12, 0), new(17, 0));
        await CreateRuleAsync(admin, firstResource.Id, DayOfWeek.Tuesday, new(9, 0), new(12, 0));
        await CreateRuleAsync(admin, secondResource.Id, DayOfWeek.Monday, new(9, 0), new(12, 0));

        var list = (await admin.GetFromJsonAsync<List<AvailabilityRuleResponse>>(firstUrl))!;
        Assert.Equal(3, list.Count);
        Assert.Equal([first.Id, adjacent.Id], list.Where(x => x.DayOfWeek == DayOfWeek.Monday).Select(x => x.Id));
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/admin/resources/{Guid.NewGuid()}/availability-rules")).StatusCode);
    }

    [Fact]
    public async Task Availability_rule_update_preserves_identity_and_failed_conflict_is_atomic()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var resource = await SeedResourceAsync();
        var first = await CreateRuleAsync(admin, resource.Id, DayOfWeek.Monday, new(9, 0), new(12, 0));
        await CreateRuleAsync(admin, resource.Id, DayOfWeek.Monday, new(13, 0), new(17, 0));

        var conflict = await admin.PutAsJsonAsync($"/api/admin/availability-rules/{first.Id}",
            new UpdateAvailabilityRuleRequest(DayOfWeek.Monday, new(14, 0), new(16, 0)));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var unchanged = (await admin.GetFromJsonAsync<List<AvailabilityRuleResponse>>(
            $"/api/admin/resources/{resource.Id}/availability-rules"))!.Single(x => x.Id == first.Id);
        Assert.Equal(new TimeOnly(9, 0), unchanged.StartTime);

        var response = await admin.PutAsJsonAsync($"/api/admin/availability-rules/{first.Id}",
            new UpdateAvailabilityRuleRequest(DayOfWeek.Tuesday, new(10, 0), new(11, 0)));
        var updated = (await response.Content.ReadFromJsonAsync<AvailabilityRuleResponse>())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(first.ResourceId, updated.ResourceId);
        Assert.Equal(first.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.IsActive);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/availability-rules/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/availability-rules/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task Blocked_periods_normalize_utc_and_reason_and_allow_boundaries()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var resource = await SeedResourceAsync();
        var url = $"/api/admin/resources/{resource.Id}/blocked-periods";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url,
            new CreateBlockedPeriodRequest(DateTimeOffset.Parse("2026-08-20T10:00:00Z"), DateTimeOffset.Parse("2026-08-20T10:00:00Z"), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url,
            new CreateBlockedPeriodRequest(DateTimeOffset.Parse("2026-08-20T10:00:00Z"), DateTimeOffset.Parse("2026-08-20T11:00:00Z"), new string('x', 501)))).StatusCode);

        var create = await admin.PostAsJsonAsync(url, new CreateBlockedPeriodRequest(
            DateTimeOffset.Parse("2026-08-20T12:00:00+03:00"), DateTimeOffset.Parse("2026-08-20T14:00:00+03:00"), "   "));
        var period = (await create.Content.ReadFromJsonAsync<BlockedPeriodResponse>())!;
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(TimeSpan.Zero, period.StartsAtUtc.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T09:00:00Z"), period.StartsAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T11:00:00Z"), period.EndsAtUtc);
        Assert.Null(period.Reason);

        var update = await admin.PutAsJsonAsync($"/api/admin/blocked-periods/{period.Id}", new UpdateBlockedPeriodRequest(
            DateTimeOffset.Parse("2026-08-21T10:00:00Z"), DateTimeOffset.Parse("2026-08-21T11:00:00Z"), "  Maintenance  "));
        var updated = (await update.Content.ReadFromJsonAsync<BlockedPeriodResponse>())!;
        Assert.Equal(period.Id, updated.Id);
        Assert.Equal(period.ResourceId, updated.ResourceId);
        Assert.Equal(period.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal("Maintenance", updated.Reason);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/blocked-periods/{period.Id}")).StatusCode);
    }

    [Fact]
    public async Task Only_overlapping_confirmed_reservations_block_create_and_update()
    {
        using var admin = await ClientForRoleAsync(AppRoles.Admin);
        var resource = await SeedReservationGraphAsync(DateTimeOffset.Parse("2026-08-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-20T10:30:00Z"), ReservationStatus.Confirmed);
        var url = $"/api/admin/resources/{resource.Id}/blocked-periods";
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(url, new CreateBlockedPeriodRequest(
            DateTimeOffset.Parse("2026-08-20T10:15:00Z"), DateTimeOffset.Parse("2026-08-20T11:00:00Z"), null))).StatusCode);

        var boundary = await admin.PostAsJsonAsync(url, new CreateBlockedPeriodRequest(
            DateTimeOffset.Parse("2026-08-20T10:30:00Z"), DateTimeOffset.Parse("2026-08-20T11:00:00Z"), null));
        Assert.Equal(HttpStatusCode.Created, boundary.StatusCode);
        var period = (await boundary.Content.ReadFromJsonAsync<BlockedPeriodResponse>())!;
        var conflict = await admin.PutAsJsonAsync($"/api/admin/blocked-periods/{period.Id}", new UpdateBlockedPeriodRequest(
            DateTimeOffset.Parse("2026-08-20T10:15:00Z"), DateTimeOffset.Parse("2026-08-20T11:00:00Z"), "changed"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var unchanged = (await admin.GetFromJsonAsync<List<BlockedPeriodResponse>>(url))!.Single(x => x.Id == period.Id);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T10:30:00Z"), unchanged.StartsAtUtc);

        var cancelledResource = await SeedReservationGraphAsync(DateTimeOffset.Parse("2026-08-20T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-20T10:30:00Z"), ReservationStatus.Cancelled);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/admin/resources/{cancelledResource.Id}/blocked-periods",
            new CreateBlockedPeriodRequest(DateTimeOffset.Parse("2026-08-20T10:15:00Z"), DateTimeOffset.Parse("2026-08-20T11:00:00Z"), null))).StatusCode);
    }

    private async Task<AvailabilityRuleResponse> CreateRuleAsync(HttpClient admin, Guid resourceId, DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        var response = await admin.PostAsJsonAsync($"/api/admin/resources/{resourceId}/availability-rules",
            new CreateAvailabilityRuleRequest(day, start, end));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AvailabilityRuleResponse>())!;
    }

    private async Task<Resource> SeedResourceAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var resource = new Resource { Id = Guid.NewGuid(), Name = $"Resource-{Guid.NewGuid():N}", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }

    private async Task<Resource> SeedReservationGraphAsync(DateTimeOffset starts, DateTimeOffset ends, ReservationStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var resource = new Resource { Id = Guid.NewGuid(), Name = $"Resource-{Guid.NewGuid():N}", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var service = new Service { Id = Guid.NewGuid(), Name = "Service", DurationMinutes = 30, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var user = NewUser(AppRoles.Customer);
        db.AddRange(resource, service, user);
        db.Reservations.Add(new Reservation { Id = Guid.NewGuid(), ReferenceNumber = Guid.NewGuid().ToString("N"), UserId = user.Id,
            ResourceId = resource.Id, ServiceId = service.Id, StartsAtUtc = starts, EndsAtUtc = ends, Status = status,
            CreatedAtUtc = now, UpdatedAtUtc = now, ServiceNameSnapshot = service.Name, ResourceNameSnapshot = resource.Name,
            ServiceDurationMinutesSnapshot = 30 });
        await db.SaveChangesAsync();
        return resource;
    }

    private async Task<HttpClient> ClientForRoleAsync(string role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = NewUser(role);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static User NewUser(string role) => new() { Id = Guid.NewGuid(), FirstName = "Test", LastName = role,
        Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "test-only",
        Role = role, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
}
