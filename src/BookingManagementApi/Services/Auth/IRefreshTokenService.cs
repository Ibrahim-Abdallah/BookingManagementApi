using BookingManagementApi.Entities;

namespace BookingManagementApi.Services.Auth;

public interface IRefreshTokenService
{
    RefreshTokenResult Create(User user);
    string Hash(string rawToken);
}

public sealed record RefreshTokenResult(string RawToken, RefreshToken Entity);
