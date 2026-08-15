namespace BookingManagementApi.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public required string TokenHash { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public User User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }
}
