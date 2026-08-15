namespace BookingManagementApi.Services.Auth;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    string? Role { get; }
}
