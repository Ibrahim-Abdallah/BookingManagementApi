using System.Security.Cryptography;
using System.Text;
using BookingManagementApi.Configuration;
using BookingManagementApi.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BookingManagementApi.Services.Auth;

public sealed class RefreshTokenService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    private readonly JwtOptions _options = options.Value;

    public RefreshTokenResult Create(User user)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var now = timeProvider.GetUtcNow();
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(rawToken),
            UserId = user.Id,
            User = user,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenExpirationDays)
        };
        return new RefreshTokenResult(rawToken, entity);
    }

    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
