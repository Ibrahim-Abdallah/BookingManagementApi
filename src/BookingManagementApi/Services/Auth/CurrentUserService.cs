using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BookingManagementApi.Services.Auth;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);
    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);
}
