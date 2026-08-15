using BookingManagementApi.Entities;

namespace BookingManagementApi.Services.Auth;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAtUtc);
