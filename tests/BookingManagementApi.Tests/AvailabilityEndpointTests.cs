using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Availability;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using BookingManagementApi.Services.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookingManagementApi.Tests;

public sealed class AvailabilityEndpointTests : IAsyncLifetime
{
    private readonly FixedTimeProvider _clock = new(DateTimeOffset.Parse("2026-08-16T08:30:00Z"));
    private AuthenticationApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new AuthenticationApiFactory { Clock = _clock };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Availability_requires_authentication_and_allows_customer()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/availability?serviceId={Guid.NewGuid()}&date=2026-08-20")).StatusCode);

        using var customer = await CustomerClientAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await customer.GetAsync($"/api/availability?serviceId={Guid.NewGuid()}&date=2026-08-20")).StatusCode);
    }

    [Fact]
    public async Task Availability_allows_admin_and_rejects_inactive_service()
    {
        var inactiveServiceId = await SeedServiceAsync(_factory, false);
        using var admin = CreateClient(_factory, AppRoles.Admin);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(
            $"/api/availability?serviceId={inactiveServiceId}&date=2026-08-20")).StatusCode);
    }

    [Fact]
    public async Task Minimum_advance_excludes_earlier_start_and_allows_exact_boundary()
    {
        var serviceId = await SeedAvailabilityAsync(_factory, new(2026, 8, 16), new(8, 45), new(9, 30), 15);
        using var customer = CreateClient(_factory, AppRoles.Customer);

        var body = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={serviceId}&date=2026-08-16"))!;

        Assert.Equal(DateTimeOffset.Parse("2026-08-16T09:00:00Z"), body.Slots[0].StartsAtUtc);
        Assert.DoesNotContain(body.Slots, x => x.StartsAtUtc == DateTimeOffset.Parse("2026-08-16T08:45:00Z"));
    }

    [Fact]
    public async Task Final_horizon_date_allows_exact_boundary_and_excludes_later_starts()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-16T08:30:00Z"));
        await using var factory = new AuthenticationApiFactory
        {
            Clock = clock,
            MinimumAdvanceMinutes = 0,
            MaximumBookingHorizonDays = 1
        };
        var serviceId = await SeedAvailabilityAsync(factory, new(2026, 8, 17), new(8, 30), new(9, 0), 15);
        using var customer = CreateClient(factory, AppRoles.Customer);

        var body = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={serviceId}&date=2026-08-17"))!;

        var slot = Assert.Single(body.Slots);
        Assert.Equal(DateTimeOffset.Parse("2026-08-17T08:30:00Z"), slot.StartsAtUtc);
    }

    [Fact]
    public async Task Forty_five_minute_service_keeps_fifteen_minute_start_interval_and_rule_boundary()
    {
        var serviceId = await SeedAvailabilityAsync(_factory, new(2026, 8, 20), new(9, 0), new(10, 30), 45);
        using var customer = CreateClient(_factory, AppRoles.Customer);

        var body = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={serviceId}&date=2026-08-20"))!;

        Assert.Equal([new TimeSpan(9, 0, 0), new(9, 15, 0), new(9, 30, 0), new(9, 45, 0)],
            body.Slots.Select(x => x.StartsAtUtc.TimeOfDay));
        Assert.All(body.Slots, x => Assert.Equal(TimeSpan.FromMinutes(45), x.EndsAtUtc - x.StartsAtUtc));
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T10:30:00Z"), body.Slots[^1].EndsAtUtc);
    }

    [Fact]
    public async Task Non_utc_business_timezone_converts_local_slots_deterministically()
    {
        var timeZoneId = ResolveTimeZone("Asia/Kolkata", "India Standard Time");
        await using var factory = new AuthenticationApiFactory
        {
            Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-08-16T08:30:00Z")),
            BusinessTimeZoneId = timeZoneId
        };
        var serviceId = await SeedAvailabilityAsync(factory, new(2026, 8, 20), new(9, 0), new(9, 30), 30);
        using var customer = CreateClient(factory, AppRoles.Customer);

        var body = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={serviceId}&date=2026-08-20"))!;

        Assert.Equal(timeZoneId, body.BusinessTimeZoneId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T03:30:00Z"), Assert.Single(body.Slots).StartsAtUtc);
    }

    [Fact]
    public async Task Dst_invalid_and_ambiguous_candidates_are_skipped_without_machine_timezone_dependency()
    {
        var timeZoneId = ResolveTimeZone("America/New_York", "Eastern Standard Time");
        await using var factory = new AuthenticationApiFactory
        {
            Clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            BusinessTimeZoneId = timeZoneId,
            MinimumAdvanceMinutes = 0,
            MaximumBookingHorizonDays = 365,
            SlotIntervalMinutes = 30
        };
        var invalidServiceId = await SeedAvailabilityAsync(factory, new(2026, 3, 8), new(1, 30), new(3, 30), 60);
        var ambiguousServiceId = await SeedAvailabilityAsync(factory, new(2026, 11, 1), new(0, 30), new(2, 30), 60);
        using var customer = CreateClient(factory, AppRoles.Customer);

        var invalid = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={invalidServiceId}&date=2026-03-08"))!;
        var ambiguous = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={ambiguousServiceId}&date=2026-11-01"))!;

        Assert.Empty(invalid.Slots);
        Assert.Empty(ambiguous.Slots);
    }

    [Fact]
    public async Task Results_are_deterministic_and_duplicate_candidate_ranges_are_suppressed()
    {
        var serviceId = await SeedOrderedDuplicateAvailabilityAsync();
        using var customer = CreateClient(_factory, AppRoles.Customer);

        var body = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={serviceId}&date=2026-08-20"))!;

        Assert.Equal(["Alpha", "Beta"], body.Slots.Select(x => x.ResourceName));
        Assert.Equal(2, body.Slots.Select(x => (x.ResourceId, x.StartsAtUtc, x.EndsAtUtc)).Distinct().Count());
    }

    [Fact]
    public async Task Search_generates_duration_based_slots_and_filters_all_operational_conflicts()
    {
        var graph = await SeedGraphAsync();
        using var customer = await CustomerClientAsync();

        var response = await customer.GetAsync($"/api/availability?serviceId={graph.ServiceId}&date=2026-08-20");
        var body = (await response.Content.ReadFromJsonAsync<AvailabilityResponse>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("UTC", body.BusinessTimeZoneId);
        Assert.Equal(graph.ServiceId, body.ServiceId);
        Assert.Equal(new[] { "2026-08-20T09:00:00Z", "2026-08-20T09:15:00Z", "2026-08-20T10:45:00Z",
            "2026-08-20T13:00:00Z", "2026-08-20T13:15:00Z", "2026-08-20T13:30:00Z" }.Select(DateTimeOffset.Parse),
            body.Slots.Select(x => x.StartsAtUtc));
        Assert.All(body.Slots, x => Assert.Equal(TimeSpan.FromMinutes(30), x.EndsAtUtc - x.StartsAtUtc));
        Assert.All(body.Slots, x => Assert.Equal(graph.ResourceId, x.ResourceId));

        using var scope = _factory.Services.CreateScope();
        var expired = scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.Single(x => x.ReferenceNumber == "EXPIRED");
        Assert.Equal(ReservationStatus.Held, expired.Status); // Search is read-only; expiry is logical only.
    }

    [Fact]
    public async Task Search_validates_dates_resource_filter_and_active_compatibility()
    {
        var graph = await SeedGraphAsync();
        using var customer = await CustomerClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await customer.GetAsync(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-08-15")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.GetAsync(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-11-15")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-08-20&resourceId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-08-20&resourceId={graph.InactiveResourceId}")).StatusCode);

        var unsupportedResponse = await customer.GetAsync(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-08-20&resourceId={graph.UnsupportedResourceId}");
        var unsupported = (await unsupportedResponse.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        Assert.Equal(HttpStatusCode.OK, unsupportedResponse.StatusCode);
        Assert.Empty(unsupported.Slots);

        var filtered = (await customer.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/availability?serviceId={graph.ServiceId}&date=2026-08-20&resourceId={graph.ResourceId}"))!;
        Assert.NotEmpty(filtered.Slots);
        Assert.All(filtered.Slots, x => Assert.Equal(graph.ResourceId, x.ResourceId));
    }

    private async Task<(Guid ServiceId, Guid ResourceId, Guid UnsupportedResourceId, Guid InactiveResourceId)> SeedGraphAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.GetUtcNow();
        var service = new Service { Id = Guid.NewGuid(), Name = "Consultation", DurationMinutes = 30, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var resource = new Resource { Id = Guid.NewGuid(), Name = "Alpha", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var unsupported = new Resource { Id = Guid.NewGuid(), Name = "Unsupported", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var inactive = new Resource { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now };
        var user = NewUser();
        db.AddRange(service, resource, unsupported, inactive, user);
        db.ResourceServices.Add(new() { ResourceId = resource.Id, ServiceId = service.Id });
        db.AvailabilityRules.AddRange(
            new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(12, 0), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now },
            new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(13, 0), EndTime = new(14, 0), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now },
            new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(15, 0), EndTime = new(16, 0), IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.BlockedPeriods.Add(new() { Id = Guid.NewGuid(), ResourceId = resource.Id, StartsAtUtc = DateTimeOffset.Parse("2026-08-20T09:45:00Z"), EndsAtUtc = DateTimeOffset.Parse("2026-08-20T10:15:00Z"), CreatedAtUtc = now });
        db.Reservations.AddRange(
            Reservation(resource, service, user, "CONFIRMED", ReservationStatus.Confirmed, "2026-08-20T10:15:00Z", "2026-08-20T10:45:00Z", null, now),
            Reservation(resource, service, user, "HELD", ReservationStatus.Held, "2026-08-20T11:15:00Z", "2026-08-20T11:45:00Z", "2026-08-16T08:35:00Z", now),
            Reservation(resource, service, user, "EXPIRED", ReservationStatus.Held, "2026-08-20T13:00:00Z", "2026-08-20T13:30:00Z", "2026-08-16T08:29:00Z", now));
        await db.SaveChangesAsync();
        return (service.Id, resource.Id, unsupported.Id, inactive.Id);
    }

    private static async Task<Guid> SeedServiceAsync(AuthenticationApiFactory factory, bool isActive)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new Service { Id = Guid.NewGuid(), Name = $"Service-{Guid.NewGuid():N}", DurationMinutes = 30,
            IsActive = isActive, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }

    private static async Task<Guid> SeedAvailabilityAsync(AuthenticationApiFactory factory, DateOnly date,
        TimeOnly start, TimeOnly end, int durationMinutes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var service = new Service { Id = Guid.NewGuid(), Name = $"Service-{Guid.NewGuid():N}", DurationMinutes = durationMinutes,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var resource = new Resource { Id = Guid.NewGuid(), Name = $"Resource-{Guid.NewGuid():N}", IsActive = true,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(service, resource);
        db.ResourceServices.Add(new() { ResourceId = resource.Id, ServiceId = service.Id });
        db.AvailabilityRules.Add(new() { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = date.DayOfWeek,
            StartTime = start, EndTime = end, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        return service.Id;
    }

    private async Task<Guid> SeedOrderedDuplicateAvailabilityAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.GetUtcNow();
        var service = new Service { Id = Guid.NewGuid(), Name = "Ordering", DurationMinutes = 30, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var beta = new Resource { Id = Guid.NewGuid(), Name = "Beta", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var alpha = new Resource { Id = Guid.NewGuid(), Name = "Alpha", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(service, beta, alpha);
        db.ResourceServices.AddRange(new() { ResourceId = beta.Id, ServiceId = service.Id }, new() { ResourceId = alpha.Id, ServiceId = service.Id });
        foreach (var resource in new[] { beta, alpha })
        {
            db.AvailabilityRules.AddRange(
                new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(9, 30), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now },
                new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resource.Id, DayOfWeek = DayOfWeek.Thursday, StartTime = new(9, 0), EndTime = new(9, 30), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync();
        return service.Id;
    }

    private static Reservation Reservation(Resource resource, Service service, User user, string reference,
        ReservationStatus status, string start, string end, string? expires, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), ReferenceNumber = reference, UserId = user.Id, ResourceId = resource.Id, ServiceId = service.Id,
        StartsAtUtc = DateTimeOffset.Parse(start), EndsAtUtc = DateTimeOffset.Parse(end), Status = status,
        HoldExpiresAtUtc = expires is null ? null : DateTimeOffset.Parse(expires), CreatedAtUtc = now, UpdatedAtUtc = now,
        ServiceNameSnapshot = service.Name, ResourceNameSnapshot = resource.Name, ServiceDurationMinutesSnapshot = service.DurationMinutes
    };

    private async Task<HttpClient> CustomerClientAsync()
    {
        await Task.CompletedTask;
        return CreateClient(_factory, AppRoles.Customer);
    }

    private static HttpClient CreateClient(AuthenticationApiFactory factory, string role)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "BookingManagementApi.Tests", "BookingManagementApi.Tests.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
             new Claim(JwtRegisteredClaimNames.Email, "availability@example.com"),
             new Claim(ClaimTypes.Role, role)],
            now.AddMinutes(-1), now.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.JwtKey)),
                SecurityAlgorithms.HmacSha256)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string ResolveTimeZone(params string[] ids)
    {
        foreach (var id in ids)
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out _)) return id;
        throw new InvalidOperationException($"None of the required test time zones are available: {string.Join(", ", ids)}");
    }

    private static User NewUser() => new() { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Customer",
        Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "test-only",
        Role = AppRoles.Customer, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
