namespace BookingManagementApi.Contracts.Reservations;

public sealed record CreateReservationHoldRequest(Guid ServiceId, Guid ResourceId, DateTimeOffset StartsAtUtc);

public sealed record ReservationHoldResponse(
    Guid ReservationId,
    string ReferenceNumber,
    string Status,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset HoldExpiresAtUtc);
