namespace BookingManagementApi.Contracts.Availability;

public sealed record AvailabilityQueryParameters(Guid ServiceId, DateOnly Date, Guid? ResourceId);

public sealed record AvailabilityResponse(
    DateOnly Date,
    string BusinessTimeZoneId,
    Guid ServiceId,
    List<AvailabilitySlotResponse> Slots);

public sealed record AvailabilitySlotResponse(
    Guid ResourceId,
    string ResourceName,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);
