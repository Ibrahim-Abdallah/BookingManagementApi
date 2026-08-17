using BookingManagementApi.Enums;

namespace BookingManagementApi.Contracts.Reservations;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages);

public class ReservationQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public ReservationStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
}

public sealed class AdminReservationQuery : ReservationQuery
{
    public string? CustomerEmail { get; init; }
    public Guid? ResourceId { get; init; }
    public Guid? ServiceId { get; init; }
}

public sealed record CancelReservationRequest(string? Reason);
public sealed record RescheduleReservationRequest(DateTimeOffset StartsAtUtc);
public sealed record UpdateReservationStatusRequest(string Status);

public record ReservationResponse(Guid ReservationId, string ReferenceNumber, Guid ServiceId, string ServiceName,
    Guid ResourceId, string ResourceName, string Status, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    DateTimeOffset? HoldExpiresAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc, DateTimeOffset? CancelledAtUtc, string? CancellationReason);

public sealed record AdminReservationResponse(Guid ReservationId, string ReferenceNumber, Guid ServiceId, string ServiceName,
    Guid ResourceId, string ResourceName, string Status, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    DateTimeOffset? HoldExpiresAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ConfirmedAtUtc, DateTimeOffset? CancelledAtUtc, string? CancellationReason,
    Guid CustomerId, string CustomerEmail, string CustomerFirstName, string CustomerLastName)
    : ReservationResponse(ReservationId, ReferenceNumber, ServiceId, ServiceName, ResourceId, ResourceName, Status,
        StartsAtUtc, EndsAtUtc, HoldExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc, ConfirmedAtUtc, CancelledAtUtc, CancellationReason);
