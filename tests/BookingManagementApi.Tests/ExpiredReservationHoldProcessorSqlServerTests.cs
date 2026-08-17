using BookingManagementApi.Common.Security;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using BookingManagementApi.Services.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingManagementApi.Tests;

public sealed class ExpiredReservationHoldProcessorSqlServerTests : IAsyncLifetime
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
    public async Task Cleanup_expires_only_expired_holds_and_is_idempotent()
    {
        var graph = await SeedGraphAsync();
        var oldUpdated = Now.AddDays(-2);
        var expiredAt = Now.AddMinutes(-1);
        var rows = new[]
        {
            Reservation(graph, "EXPIRED-HELD", ReservationStatus.Held, expiredAt, oldUpdated),
            Reservation(graph, "FUTURE-HELD", ReservationStatus.Held, Now.AddMinutes(1), oldUpdated),
            Reservation(graph, "CONFIRMED", ReservationStatus.Confirmed, expiredAt, oldUpdated),
            Reservation(graph, "CANCELLED", ReservationStatus.Cancelled, expiredAt, oldUpdated),
            Reservation(graph, "COMPLETED", ReservationStatus.Completed, expiredAt, oldUpdated),
            Reservation(graph, "NOSHOW", ReservationStatus.NoShow, expiredAt, oldUpdated),
            Reservation(graph, "ALREADY-EXPIRED", ReservationStatus.Expired, expiredAt, oldUpdated)
        };
        await AddReservationsAsync(rows);

        Assert.Equal(1, await ProcessAsync());
        Assert.Equal(0, await ProcessAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations
            .AsNoTracking().ToDictionaryAsync(x => x.ReferenceNumber);
        Assert.Equal(ReservationStatus.Expired, stored["EXPIRED-HELD"].Status);
        Assert.Equal(Now, stored["EXPIRED-HELD"].UpdatedAtUtc);
        Assert.Equal(expiredAt, stored["EXPIRED-HELD"].HoldExpiresAtUtc);
        Assert.Equal(ReservationStatus.Held, stored["FUTURE-HELD"].Status);
        Assert.Equal(ReservationStatus.Confirmed, stored["CONFIRMED"].Status);
        Assert.Equal(ReservationStatus.Cancelled, stored["CANCELLED"].Status);
        Assert.Equal(ReservationStatus.Completed, stored["COMPLETED"].Status);
        Assert.Equal(ReservationStatus.NoShow, stored["NOSHOW"].Status);
        Assert.Equal(ReservationStatus.Expired, stored["ALREADY-EXPIRED"].Status);
        Assert.All(stored.Where(x => x.Key != "EXPIRED-HELD"), x => Assert.Equal(oldUpdated, x.Value.UpdatedAtUtc));
    }

    [Fact]
    public async Task Cleanup_processes_at_most_one_hundred_rows_per_call()
    {
        var graph = await SeedGraphAsync();
        await AddReservationsAsync(Enumerable.Range(0, 105)
            .Select(i => Reservation(graph, $"BATCH-{i:000}", ReservationStatus.Held, Now.AddMinutes(-i - 1), Now.AddDays(-1)))
            .ToArray());

        Assert.Equal(100, await ProcessAsync());
        Assert.Equal(5, await CountExpiredHeldAsync());
        Assert.Equal(5, await ProcessAsync());
        Assert.Equal(0, await ProcessAsync());
        Assert.Equal(0, await CountExpiredHeldAsync());
    }

    private async Task<int> ProcessAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ExpiredReservationHoldProcessor>().ProcessBatchAsync();
    }

    private async Task<int> CountExpiredHeldAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations
            .CountAsync(x => x.Status == ReservationStatus.Held && x.HoldExpiresAtUtc <= Now);
    }

    private async Task AddReservationsAsync(params Reservation[] reservations)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Reservations.AddRange(reservations);
        await db.SaveChangesAsync();
    }

    private async Task<Graph> SeedGraphAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User", Email = $"{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM", PasswordHash = "x", Role = AppRoles.Customer, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var service = new Service { Id = Guid.NewGuid(), Name = "Cleanup Service", DurationMinutes = 30, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var resource = new Resource { Id = Guid.NewGuid(), Name = "Cleanup Resource", IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        db.AddRange(user, service, resource);
        await db.SaveChangesAsync();
        return new(user.Id, service.Id, resource.Id);
    }

    private static Reservation Reservation(Graph graph, string reference, ReservationStatus status, DateTimeOffset? holdExpires, DateTimeOffset updated) => new()
    {
        Id = Guid.NewGuid(), ReferenceNumber = reference, UserId = graph.UserId, ServiceId = graph.ServiceId, ResourceId = graph.ResourceId,
        StartsAtUtc = Now.AddDays(1), EndsAtUtc = Now.AddDays(1).AddMinutes(30), Status = status, HoldExpiresAtUtc = holdExpires,
        CreatedAtUtc = Now.AddDays(-2), UpdatedAtUtc = updated, ServiceNameSnapshot = "Cleanup Service",
        ResourceNameSnapshot = "Cleanup Resource", ServiceDurationMinutesSnapshot = 30
    };

    private sealed record Graph(Guid UserId, Guid ServiceId, Guid ResourceId);
}
