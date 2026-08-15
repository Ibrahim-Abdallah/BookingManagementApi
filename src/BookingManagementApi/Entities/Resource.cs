namespace BookingManagementApi.Entities;

public sealed class Resource
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<ResourceService> ResourceServices { get; } = [];
    public ICollection<AvailabilityRule> AvailabilityRules { get; } = [];
    public ICollection<BlockedPeriod> BlockedPeriods { get; } = [];
    public ICollection<Reservation> Reservations { get; } = [];
}
