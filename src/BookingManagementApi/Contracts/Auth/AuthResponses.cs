namespace BookingManagementApi.Contracts.Auth;

public sealed record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    DateTimeOffset CreatedAtUtc);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    UserResponse User);

public sealed record ErrorResponse(string Error);
