using BookingManagementApi.Contracts.Auth;

namespace BookingManagementApi.Services.Auth;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public sealed record RegisterResult(UserResponse? User, bool IsDuplicateEmail);
