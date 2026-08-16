using BookingManagementApi.Contracts.Scheduling;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Scheduling;

public enum ScheduleResult { Success, ResourceNotFound, ItemNotFound, Conflict }
public sealed record ScheduleOperation<T>(ScheduleResult Result, T? Value = default);

public sealed class ScheduleManagementService(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<ScheduleOperation<List<AvailabilityRuleResponse>>> ListAvailabilityRulesAsync(Guid resourceId, CancellationToken ct)
    {
        if (!await ResourceExists(resourceId, ct)) return new(ScheduleResult.ResourceNotFound);
        var rules = await db.AvailabilityRules.AsNoTracking().Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime).ThenBy(x => x.Id)
            .Select(x => Map(x)).ToListAsync(ct);
        return new(ScheduleResult.Success, rules);
    }

    public async Task<ScheduleOperation<AvailabilityRuleResponse>> CreateAvailabilityRuleAsync(Guid resourceId, CreateAvailabilityRuleRequest request, CancellationToken ct)
    {
        if (!await ResourceExists(resourceId, ct)) return new(ScheduleResult.ResourceNotFound);
        if (await HasRuleOverlap(resourceId, request.DayOfWeek, request.StartTime, request.EndTime, null, ct))
            return new(ScheduleResult.Conflict);
        var now = timeProvider.GetUtcNow();
        var rule = new AvailabilityRule { Id = Guid.NewGuid(), ResourceId = resourceId, DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime, EndTime = request.EndTime, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AvailabilityRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return new(ScheduleResult.Success, Map(rule));
    }

    public async Task<ScheduleOperation<AvailabilityRuleResponse>> UpdateAvailabilityRuleAsync(Guid id, UpdateAvailabilityRuleRequest request, CancellationToken ct)
    {
        var rule = await db.AvailabilityRules.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rule is null) return new(ScheduleResult.ItemNotFound);
        if (await HasRuleOverlap(rule.ResourceId, request.DayOfWeek, request.StartTime, request.EndTime, id, ct))
            return new(ScheduleResult.Conflict);
        rule.DayOfWeek = request.DayOfWeek;
        rule.StartTime = request.StartTime;
        rule.EndTime = request.EndTime;
        rule.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new(ScheduleResult.Success, Map(rule));
    }

    public async Task<ScheduleResult> DeleteAvailabilityRuleAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.AvailabilityRules.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rule is null) return ScheduleResult.ItemNotFound;
        db.AvailabilityRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return ScheduleResult.Success;
    }

    public async Task<ScheduleOperation<List<BlockedPeriodResponse>>> ListBlockedPeriodsAsync(Guid resourceId, CancellationToken ct)
    {
        if (!await ResourceExists(resourceId, ct)) return new(ScheduleResult.ResourceNotFound);
        var periods = await db.BlockedPeriods.AsNoTracking().Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.EndsAtUtc).ThenBy(x => x.Id)
            .Select(x => Map(x)).ToListAsync(ct);
        return new(ScheduleResult.Success, periods);
    }

    public async Task<ScheduleOperation<BlockedPeriodResponse>> CreateBlockedPeriodAsync(Guid resourceId, CreateBlockedPeriodRequest request, CancellationToken ct)
    {
        if (!await ResourceExists(resourceId, ct)) return new(ScheduleResult.ResourceNotFound);
        var starts = request.StartsAtUtc.ToUniversalTime();
        var ends = request.EndsAtUtc.ToUniversalTime();
        if (await HasConfirmedReservationConflict(resourceId, starts, ends, ct)) return new(ScheduleResult.Conflict);
        var period = new BlockedPeriod { Id = Guid.NewGuid(), ResourceId = resourceId, StartsAtUtc = starts,
            EndsAtUtc = ends, Reason = NormalizeReason(request.Reason), CreatedAtUtc = timeProvider.GetUtcNow() };
        db.BlockedPeriods.Add(period);
        await db.SaveChangesAsync(ct);
        return new(ScheduleResult.Success, Map(period));
    }

    public async Task<ScheduleOperation<BlockedPeriodResponse>> UpdateBlockedPeriodAsync(Guid id, UpdateBlockedPeriodRequest request, CancellationToken ct)
    {
        var period = await db.BlockedPeriods.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (period is null) return new(ScheduleResult.ItemNotFound);
        var starts = request.StartsAtUtc.ToUniversalTime();
        var ends = request.EndsAtUtc.ToUniversalTime();
        if (await HasConfirmedReservationConflict(period.ResourceId, starts, ends, ct)) return new(ScheduleResult.Conflict);
        period.StartsAtUtc = starts;
        period.EndsAtUtc = ends;
        period.Reason = NormalizeReason(request.Reason);
        await db.SaveChangesAsync(ct);
        return new(ScheduleResult.Success, Map(period));
    }

    public async Task<ScheduleResult> DeleteBlockedPeriodAsync(Guid id, CancellationToken ct)
    {
        var period = await db.BlockedPeriods.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (period is null) return ScheduleResult.ItemNotFound;
        db.BlockedPeriods.Remove(period);
        await db.SaveChangesAsync(ct);
        return ScheduleResult.Success;
    }

    private Task<bool> ResourceExists(Guid id, CancellationToken ct) => db.Resources.AsNoTracking().AnyAsync(x => x.Id == id, ct);
    private Task<bool> HasRuleOverlap(Guid resourceId, DayOfWeek day, TimeOnly start, TimeOnly end, Guid? excludedId, CancellationToken ct) =>
        db.AvailabilityRules.AsNoTracking().AnyAsync(x => x.ResourceId == resourceId && x.DayOfWeek == day &&
            (!excludedId.HasValue || x.Id != excludedId.Value) && x.StartTime < end && x.EndTime > start, ct);
    private Task<bool> HasConfirmedReservationConflict(Guid resourceId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        db.Reservations.AsNoTracking().AnyAsync(x => x.ResourceId == resourceId && x.Status == ReservationStatus.Confirmed &&
            x.StartsAtUtc < end && x.EndsAtUtc > start, ct);
    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    private static AvailabilityRuleResponse Map(AvailabilityRule x) => new(x.Id, x.ResourceId, x.DayOfWeek, x.StartTime, x.EndTime, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static BlockedPeriodResponse Map(BlockedPeriod x) => new(x.Id, x.ResourceId, x.StartsAtUtc, x.EndsAtUtc, x.Reason, x.CreatedAtUtc);
}
