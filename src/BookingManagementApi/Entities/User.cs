namespace BookingManagementApi.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
    public ICollection<Reservation> Reservations { get; } = [];
}
