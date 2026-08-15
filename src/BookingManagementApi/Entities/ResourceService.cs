namespace BookingManagementApi.Entities;

public sealed class ResourceService
{
    public Guid ResourceId { get; set; }
    public Guid ServiceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
