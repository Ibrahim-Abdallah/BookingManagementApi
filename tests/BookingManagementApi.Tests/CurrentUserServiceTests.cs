using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingManagementApi.Services.Auth;
using Microsoft.AspNetCore.Http;

namespace BookingManagementApi.Tests;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public void Extracts_valid_authenticated_claims()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, "customer@example.com"),
            new Claim(ClaimTypes.Role, "Customer")
        ], "Bearer"));
        var service = CreateService(principal);

        Assert.True(service.IsAuthenticated);
        Assert.Equal(id, service.UserId);
        Assert.Equal("customer@example.com", service.Email);
        Assert.Equal("Customer", service.Role);
    }

    [Fact]
    public void Malformed_or_missing_identity_fails_safely()
    {
        var service = CreateService(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid")])));

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.UserId);
        Assert.Null(service.Email);
        Assert.Null(service.Role);
    }

    private static CurrentUserService CreateService(ClaimsPrincipal principal) => new(
        new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
}
