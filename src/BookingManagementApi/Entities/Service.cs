namespace BookingManagementApi.Entities;

public sealed class Service
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<ResourceService> ResourceServices { get; } = [];
    public ICollection<Reservation> Reservations { get; } = [];
}
