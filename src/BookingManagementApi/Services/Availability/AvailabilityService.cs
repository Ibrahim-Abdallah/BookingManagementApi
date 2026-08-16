using BookingManagementApi.Configuration;
using BookingManagementApi.Contracts.Availability;
using BookingManagementApi.Data;
using BookingManagementApi.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.Services.Availability;

public sealed class AvailabilityService(
    AppDbContext db,
    IOptions<SchedulingOptions> options,
    TimeProvider timeProvider)
{
    private readonly SchedulingOptions _options = options.Value;

    public async Task<AvailabilityOperation> SearchAsync(
        AvailabilityQueryParameters query,
        CancellationToken ct)
    {
        var service = await db.Services.AsNoTracking()
            .Where(x => x.Id == query.ServiceId && x.IsActive)
            .Select(x => new { x.Id, x.DurationMinutes })
            .SingleOrDefaultAsync(ct);
        if (service is null) return new(AvailabilityResult.ServiceNotFound);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.BusinessTimeZoneId);
        var nowUtc = timeProvider.GetUtcNow();
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);
        var latestAllowedStartUtc = nowUtc.AddDays(_options.MaximumBookingHorizonDays);
        var latestLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(latestAllowedStartUtc, timeZone).DateTime);
        if (query.Date < localToday || query.Date > latestLocalDate)
            return new(AvailabilityResult.InvalidDate);

        if (query.ResourceId.HasValue)
        {
            var resourceState = await db.Resources.AsNoTracking()
                .Where(x => x.Id == query.ResourceId.Value)
                .Select(x => new { x.IsActive })
                .SingleOrDefaultAsync(ct);
            if (resourceState is null || !resourceState.IsActive)
                return new(AvailabilityResult.ResourceNotFound);
        }

        var resourcesQuery = db.Resources.AsNoTracking().Where(x =>
            x.IsActive && x.ResourceServices.Any(rs => rs.ServiceId == service.Id));
        if (query.ResourceId.HasValue)
            resourcesQuery = resourcesQuery.Where(x => x.Id == query.ResourceId.Value);

        var resources = await resourcesQuery
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        var resourceIds = resources.Select(x => x.Id).ToList();
        var names = resources.ToDictionary(x => x.Id, x => x.Name);

        var rules = await db.AvailabilityRules.AsNoTracking()
            .Where(x => resourceIds.Contains(x.ResourceId) && x.IsActive && x.DayOfWeek == query.Date.DayOfWeek)
            .OrderBy(x => x.ResourceId).ThenBy(x => x.StartTime).ThenBy(x => x.Id)
            .Select(x => new { x.ResourceId, x.StartTime, x.EndTime })
            .ToListAsync(ct);

        var earliestAllowedUtc = nowUtc.AddMinutes(_options.MinimumAdvanceMinutes);
        var candidates = new List<AvailabilitySlotResponse>();
        foreach (var rule in rules)
        {
            var ruleDurationMinutes = (rule.EndTime - rule.StartTime).TotalMinutes;
            for (var offsetMinutes = 0;
                 offsetMinutes + service.DurationMinutes <= ruleDurationMinutes;
                 offsetMinutes += _options.SlotIntervalMinutes)
            {
                var start = rule.StartTime.AddMinutes(offsetMinutes);
                var localStart = query.Date.ToDateTime(start, DateTimeKind.Unspecified);
                var localEnd = localStart.AddMinutes(service.DurationMinutes);
                if (!TryConvertToUtc(localStart, timeZone, out var startsAtUtc) ||
                    !TryConvertToUtc(localEnd, timeZone, out var endsAtUtc) ||
                    endsAtUtc - startsAtUtc != TimeSpan.FromMinutes(service.DurationMinutes) ||
                    startsAtUtc < earliestAllowedUtc || startsAtUtc > latestAllowedStartUtc)
                    continue;

                candidates.Add(new(rule.ResourceId, names[rule.ResourceId], startsAtUtc, endsAtUtc));
            }
        }

        candidates = candidates
            .DistinctBy(x => (x.ResourceId, x.StartsAtUtc, x.EndsAtUtc))
            .ToList();

        if (candidates.Count > 0)
        {
            var minStart = candidates.Min(x => x.StartsAtUtc);
            var maxEnd = candidates.Max(x => x.EndsAtUtc);
            var blocks = await db.BlockedPeriods.AsNoTracking()
                .Where(x => resourceIds.Contains(x.ResourceId) && x.StartsAtUtc < maxEnd && x.EndsAtUtc > minStart)
                .Select(x => new { x.ResourceId, x.StartsAtUtc, x.EndsAtUtc })
                .ToListAsync(ct);
            var reservations = await db.Reservations.AsNoTracking()
                .Where(x => resourceIds.Contains(x.ResourceId) && x.StartsAtUtc < maxEnd && x.EndsAtUtc > minStart &&
                    (x.Status == ReservationStatus.Confirmed ||
                     (x.Status == ReservationStatus.Held && x.HoldExpiresAtUtc > nowUtc)))
                .Select(x => new { x.ResourceId, x.StartsAtUtc, x.EndsAtUtc })
                .ToListAsync(ct);

            candidates = candidates.Where(slot =>
                !blocks.Any(x => x.ResourceId == slot.ResourceId && x.StartsAtUtc < slot.EndsAtUtc && x.EndsAtUtc > slot.StartsAtUtc) &&
                !reservations.Any(x => x.ResourceId == slot.ResourceId && x.StartsAtUtc < slot.EndsAtUtc && x.EndsAtUtc > slot.StartsAtUtc))
                .ToList();
        }

        var ordered = candidates.OrderBy(x => x.StartsAtUtc)
            .ThenBy(x => x.ResourceName, StringComparer.Ordinal)
            .ThenBy(x => x.ResourceId)
            .ToList();
        return new(AvailabilityResult.Success,
            new(query.Date, _options.BusinessTimeZoneId, service.Id, ordered));
    }

    // Version 1 rejects DST gaps/folds rather than guessing which local instant was intended.
    internal static bool TryConvertToUtc(DateTime local, TimeZoneInfo timeZone, out DateTimeOffset utc)
    {
        if (timeZone.IsInvalidTime(local) || timeZone.IsAmbiguousTime(local))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
        return true;
    }
}

public enum AvailabilityResult
{
    Success,
    InvalidDate,
    ServiceNotFound,
    ResourceNotFound
}

public sealed record AvailabilityOperation(AvailabilityResult Result, AvailabilityResponse? Response = null);
