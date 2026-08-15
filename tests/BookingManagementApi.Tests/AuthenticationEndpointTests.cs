using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Auth;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using BookingManagementApi.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BookingManagementApi.Tests;

public sealed class AuthenticationEndpointTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationEndpointTests(AuthenticationApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task OpenApi_exposes_bearer_scheme_without_global_security_requirement()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"Bearer\"", document);
        Assert.Contains("\"scheme\": \"bearer\"", document);
        Assert.DoesNotContain("\"security\":", document);
    }

    [Fact]
    public async Task Register_persists_normalized_customer_with_verifiable_hash()
    {
        var password = "Valid1!Password";
        var email = $" Person.{Guid.NewGuid():N}@Example.com ";
        var response = await RegisterAsync(email, password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal(AppRoles.Customer, body.Role);
        Assert.Equal(email.Trim(), body.Email);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = Assert.Single(db.Users.Where(x => x.Id == body.Id));
        Assert.Equal(email.Trim().ToUpperInvariant(), user.NormalizedEmail);
        Assert.NotEqual(password, user.PasswordHash);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash, password));
    }

    [Fact]
    public async Task Register_rejects_duplicate_email_ignoring_case_and_whitespace()
    {
        var local = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync($"{local}@example.com")).StatusCode);

        var duplicate = await RegisterAsync($" {local.ToUpperInvariant()}@EXAMPLE.COM ");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "Valid1!Password", "Valid1!Password")]
    [InlineData("valid@example.com", "weak", "weak")]
    [InlineData("valid@example.com", "Valid1!Password", "Different1!")]
    public async Task Register_rejects_invalid_requests(string email, string password, string confirmation)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Test", "Customer", email, password, confirmation));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ignores_attempted_admin_role()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Test", lastName = "Customer", email = $"{Guid.NewGuid():N}@example.com",
            password = "Valid1!Password", confirmPassword = "Valid1!Password", role = AppRoles.Admin
        });

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(AppRoles.Customer, body!.Role);
    }

    [Fact]
    public async Task Login_with_case_insensitive_email_returns_valid_signed_jwt()
    {
        var local = Guid.NewGuid().ToString("N");
        await RegisterAsync($"{local}@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"{local.ToUpperInvariant()}@EXAMPLE.COM", "Valid1!Password"));
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        var principal = ValidateToken(auth.AccessToken, out var token);
        Assert.Equal(auth.User.Id.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(auth.User.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(AppRoles.Customer, principal.FindFirstValue(ClaimTypes.Role));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));
        Assert.Equal("BookingManagementApi.Tests", token.Issuer);
        Assert.Contains("BookingManagementApi.Tests.Client", token.Audiences);
        Assert.Equal(auth.AccessTokenExpiresAtUtc.ToUnixTimeSeconds(), new DateTimeOffset(token.ValidTo).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Login_persists_only_refresh_token_hash_with_configured_expiration()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);

        var first = await LoginAsync(email);
        var second = await LoginAsync(email);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var sessions = await db.RefreshTokens.Where(x => x.UserId == first.User.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        var stored = Assert.Single(sessions, x => x.TokenHash == hasher.Hash(first.RefreshToken));
        Assert.NotEqual(first.RefreshToken, stored.TokenHash);
        Assert.Equal(first.RefreshTokenExpiresAtUtc, stored.ExpiresAtUtc);
        Assert.Equal(7, (stored.ExpiresAtUtc - stored.CreatedAtUtc).TotalDays);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_replay_cannot_branch_chain()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);
        var tokenA = await LoginAsync(email);

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(tokenA.RefreshToken));
        var tokenB = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotNull(tokenB);
        Assert.NotEqual(tokenA.AccessToken, tokenB.AccessToken);
        Assert.NotEqual(tokenA.RefreshToken, tokenB.RefreshToken);
        var principal = ValidateToken(tokenB.AccessToken, out var jwt);
        Assert.Equal(tokenA.User.Id.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(tokenA.User.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(AppRoles.Customer, principal.FindFirstValue(ClaimTypes.Role));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));
        Assert.Equal("BookingManagementApi.Tests", jwt.Issuer);
        Assert.Contains("BookingManagementApi.Tests.Client", jwt.Audiences);
        Assert.Equal(tokenB.AccessTokenExpiresAtUtc.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());

        var replay = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(tokenA.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var old = await db.RefreshTokens.SingleAsync(x => x.TokenHash == hasher.Hash(tokenA.RefreshToken));
        var replacement = await db.RefreshTokens.SingleAsync(x => x.TokenHash == hasher.Hash(tokenB.RefreshToken));
        Assert.NotNull(old.RevokedAtUtc);
        Assert.Equal(replacement.Id, old.ReplacedByTokenId);
        Assert.Null(replacement.RevokedAtUtc);
        Assert.Equal(old.UserId, replacement.UserId);
        Assert.Equal(2, await db.RefreshTokens.CountAsync(x => x.UserId == old.UserId));
    }

    [Fact]
    public async Task Refresh_rejects_unknown_expired_and_revoked_tokens_with_generic_error()
    {
        var unknown = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest("unknown-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("Invalid refresh token.", (await unknown.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);

        var email = $"{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);
        var expired = await LoginAsync(email);
        var revoked = await LoginAsync(email);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            (await db.RefreshTokens.SingleAsync(x => x.TokenHash == hasher.Hash(expired.RefreshToken))).ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            (await db.RefreshTokens.SingleAsync(x => x.TokenHash == hasher.Hash(revoked.RefreshToken))).RevokedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(expired.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(revoked.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent_and_scoped_to_submitted_session()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await RegisterAsync(email);
        var sessionA = await LoginAsync(email);
        var sessionB = await LoginAsync(email);

        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(sessionA.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(sessionA.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest("unknown-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(sessionA.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(sessionB.RefreshToken))).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refresh_token_request_is_validated(string value)
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(value))).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_invalid_credentials_use_same_unauthorized_response(bool existingEmail)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        if (existingEmail) await RegisterAsync(email);
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, existingEmail ? "Wrong1!Password" : "Valid1!Password"));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.", error!.Error);
    }

    [Fact]
    public async Task Login_validation_does_not_apply_registration_complexity_rules()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("valid@example.com", "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string password = "Valid1!Password") =>
        _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Test", "Customer", email, password, password));

    private async Task<AuthResponse> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Valid1!Password"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static ClaimsPrincipal ValidateToken(string encodedToken, out JwtSecurityToken token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "BookingManagementApi.Tests",
            ValidateAudience = true,
            ValidAudience = "BookingManagementApi.Tests.Client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthenticationApiFactory.JwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = ClaimTypes.Role
        };
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(encodedToken, parameters, out var validatedToken);
        token = Assert.IsType<JwtSecurityToken>(validatedToken);
        return principal;
    }
}
