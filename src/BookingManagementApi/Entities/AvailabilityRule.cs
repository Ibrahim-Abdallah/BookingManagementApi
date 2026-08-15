namespace BookingManagementApi.Entities;

public sealed class AvailabilityRule
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Resource Resource { get; set; } = null!;
}
