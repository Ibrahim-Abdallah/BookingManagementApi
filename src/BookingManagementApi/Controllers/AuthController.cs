using BookingManagementApi.Contracts.Auth;
using BookingManagementApi.Common.Errors;
using BookingManagementApi.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingManagementApi.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [EndpointSummary("Register a customer account")]
    [EndpointDescription("Creates a Customer account. Public registration cannot assign the Admin role.")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return result.IsDuplicateEmail
            ? Conflict(ApiProblems.Create(409, "Account conflict.", "An account with this email already exists."))
            : StatusCode(StatusCodes.Status201Created, result.User);
    }

    [HttpPost("login")]
    [EndpointSummary("Sign in")]
    [EndpointDescription("Validates customer credentials and returns an access token with a rotating refresh token.")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(new ErrorResponse("Invalid email or password."))
            : Ok(response);
    }

    [HttpPost("refresh-token")]
    [EndpointSummary("Rotate a refresh token")]
    [EndpointDescription("Revokes the submitted refresh token and returns a new access/refresh token pair.")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RefreshAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(new ErrorResponse("Invalid refresh token."))
            : Ok(response);
    }

    [HttpPost("logout")]
    [EndpointSummary("Revoke a refresh token")]
    [EndpointDescription("Revokes the submitted active refresh token. Repeated logout remains safe.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }
}
