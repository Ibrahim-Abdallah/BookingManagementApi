namespace BookingManagementApi.Entities;

public sealed class BlockedPeriod
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Resource Resource { get; set; } = null!;
}
