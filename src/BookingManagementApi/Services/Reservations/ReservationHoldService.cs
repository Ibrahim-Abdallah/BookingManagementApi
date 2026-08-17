using BookingManagementApi.Configuration;
using BookingManagementApi.Contracts.Reservations;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using BookingManagementApi.Services.Auth;
using BookingManagementApi.Services.Availability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.Services.Reservations;

public sealed class ReservationHoldService(
    AppDbContext db,
    ICurrentUserService currentUser,
    IOptions<SchedulingOptions> options,
    TimeProvider timeProvider,
    ILogger<ReservationHoldService> logger)
{
    private readonly SchedulingOptions _options = options.Value;

    public async Task<ReservationHoldOperation> CreateAsync(CreateReservationHoldRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return new(new ReservationHoldResult.Unauthorized());
        if (request.StartsAtUtc.Offset != TimeSpan.Zero)
            return Invalid("StartsAtUtc must be expressed in UTC.");

        var preflightNow = timeProvider.GetUtcNow();
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ServiceId, ct);
        if (service is null) return new(new ReservationHoldResult.ServiceNotFound());
        if (!service.IsActive) return Invalid("The selected service is inactive.");
        var resource = await db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ResourceId, ct);
        if (resource is null) return new(new ReservationHoldResult.ResourceNotFound());
        if (!resource.IsActive) return Invalid("The selected resource is inactive.");
        if (!await db.ResourceServices.AsNoTracking().AnyAsync(
                x => x.ResourceId == resource.Id && x.ServiceId == service.Id, ct))
            return Invalid("The selected resource does not support this service.");

        var endsAtUtc = request.StartsAtUtc.AddMinutes(service.DurationMinutes);
        var earliest = preflightNow.AddMinutes(_options.MinimumAdvanceMinutes);
        var latest = preflightNow.AddDays(_options.MaximumBookingHorizonDays);
        if (request.StartsAtUtc < earliest) return Invalid("The requested start is inside the minimum advance window.");
        if (request.StartsAtUtc > latest) return Invalid("The requested start exceeds the booking horizon.");

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.BusinessTimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(request.StartsAtUtc, timeZone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(endsAtUtc, timeZone).DateTime;
        if (timeZone.IsInvalidTime(localStart) || timeZone.IsAmbiguousTime(localStart) ||
            timeZone.IsInvalidTime(localEnd) || timeZone.IsAmbiguousTime(localEnd) ||
            !AvailabilityService.TryConvertToUtc(localStart, timeZone, out var roundTripStart) ||
            !AvailabilityService.TryConvertToUtc(localEnd, timeZone, out var roundTripEnd) ||
            roundTripStart != request.StartsAtUtc || roundTripEnd != endsAtUtc ||
            (localStart.TimeOfDay.TotalMinutes % _options.SlotIntervalMinutes) != 0)
            return Invalid("The requested start is not a valid aligned slot in the business time zone.");

        var fitsRule = await db.AvailabilityRules.AsNoTracking().AnyAsync(x =>
            x.ResourceId == resource.Id && x.IsActive && x.DayOfWeek == localStart.DayOfWeek &&
            x.StartTime <= TimeOnly.FromDateTime(localStart) && x.EndTime >= TimeOnly.FromDateTime(localEnd) &&
            localStart.Date == localEnd.Date, ct);
        if (!fitsRule) return Invalid("The requested interval is outside weekly availability.");
        if (await db.BlockedPeriods.AsNoTracking().AnyAsync(x => x.ResourceId == resource.Id &&
                x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > request.StartsAtUtc, ct))
            return Invalid("The requested interval overlaps a blocked period.");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        if (transaction is not null)
        {
            await db.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM Resources WITH (UPDLOCK, HOLDLOCK) WHERE Id = {resource.Id}")
                .SingleAsync(ct);
        }

        var authoritativeNow = timeProvider.GetUtcNow();
        if (request.StartsAtUtc < authoritativeNow.AddMinutes(_options.MinimumAdvanceMinutes))
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            return Invalid("The requested start is inside the minimum advance window.");
        }
        if (request.StartsAtUtc > authoritativeNow.AddDays(_options.MaximumBookingHorizonDays))
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            return Invalid("The requested start exceeds the booking horizon.");
        }

        var conflicts = await db.Reservations.AsNoTracking().AnyAsync(x =>
            x.ResourceId == resource.Id && x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > request.StartsAtUtc &&
            (x.Status == ReservationStatus.Confirmed ||
             (x.Status == ReservationStatus.Held && x.HoldExpiresAtUtc > authoritativeNow)), ct);
        if (conflicts)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            logger.LogInformation("Booking conflict for resource {ResourceId} at {StartsAtUtc}", resource.Id, request.StartsAtUtc);
            return new(new ReservationHoldResult.Conflict());
        }

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(), ReferenceNumber = CreateReference(request.StartsAtUtc), UserId = userId,
            ResourceId = resource.Id, ServiceId = service.Id, StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = endsAtUtc, Status = ReservationStatus.Held,
            HoldExpiresAtUtc = authoritativeNow.AddMinutes(_options.HoldDurationMinutes),
            CreatedAtUtc = authoritativeNow, UpdatedAtUtc = authoritativeNow,
            ServiceNameSnapshot = service.Name, ResourceNameSnapshot = resource.Name,
            ServiceDurationMinutesSnapshot = service.DurationMinutes
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        logger.LogInformation("Reservation hold {ReservationId} created for resource {ResourceId}", reservation.Id, resource.Id);
        return new(new ReservationHoldResult.Success(new(reservation.Id, reservation.ReferenceNumber,
            reservation.Status.ToString(), reservation.StartsAtUtc, reservation.EndsAtUtc, reservation.HoldExpiresAtUtc.Value)));
    }

    private static ReservationHoldOperation Invalid(string detail) => new(new ReservationHoldResult.Invalid(detail));
    private static string CreateReference(DateTimeOffset start) => $"BKG-{start:yyyyMMdd}-{Guid.NewGuid():N}"[..29].ToUpperInvariant();
}

public abstract record ReservationHoldResult
{
    public sealed record Success(ReservationHoldResponse Response) : ReservationHoldResult;
    public sealed record Invalid(string Detail) : ReservationHoldResult;
    public sealed record ServiceNotFound : ReservationHoldResult;
    public sealed record ResourceNotFound : ReservationHoldResult;
    public sealed record Conflict : ReservationHoldResult;
    public sealed record Unauthorized : ReservationHoldResult;
}

public sealed record ReservationHoldOperation(ReservationHoldResult Result);
