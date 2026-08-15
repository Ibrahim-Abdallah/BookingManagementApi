using BookingManagementApi.Enums;

namespace BookingManagementApi.Entities;

public sealed class Reservation
{
    public Guid Id { get; set; }
    public required string ReferenceNumber { get; set; }
    public Guid UserId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid ServiceId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTimeOffset? HoldExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public required string ServiceNameSnapshot { get; set; }
    public required string ResourceNameSnapshot { get; set; }
    public int ServiceDurationMinutesSnapshot { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public User User { get; set; } = null!;
    public Resource Resource { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
