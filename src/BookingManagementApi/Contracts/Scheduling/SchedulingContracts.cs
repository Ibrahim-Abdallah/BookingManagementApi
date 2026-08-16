namespace BookingManagementApi.Contracts.Scheduling;

public sealed record CreateAvailabilityRuleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record UpdateAvailabilityRuleRequest(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
public sealed record AvailabilityRuleResponse(Guid Id, Guid ResourceId, DayOfWeek DayOfWeek, TimeOnly StartTime,
    TimeOnly EndTime, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CreateBlockedPeriodRequest(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Reason);
public sealed record UpdateBlockedPeriodRequest(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Reason);
public sealed record BlockedPeriodResponse(Guid Id, Guid ResourceId, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, string? Reason, DateTimeOffset CreatedAtUtc);
