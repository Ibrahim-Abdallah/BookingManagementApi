using BookingManagementApi.Common.Security;
using BookingManagementApi.Contracts.Auth;
using BookingManagementApi.Data;
using BookingManagementApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BookingManagementApi.Services.Auth;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        if (await dbContext.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return new RegisterResult(null, true);
        }

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = EmailNormalizer.ToDisplayEmail(request.Email),
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = AppRoles.Customer,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return new RegisterResult(null, true);
        }

        return new RegisterResult(ToResponse(user), false);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            x => x.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = refreshTokenService.Create(user);
        dbContext.RefreshTokens.Add(refreshToken.Entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAuthResponse(user, accessToken, refreshToken);
    }

    public async Task<AuthResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = refreshTokenService.Hash(request.RefreshToken);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var storedToken = await FindForUpdateAsync(tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (storedToken is null || storedToken.RevokedAtUtc is not null || storedToken.ExpiresAtUtc <= now)
        {
            return null;
        }

        var replacement = refreshTokenService.Create(storedToken.User);
        storedToken.RevokedAtUtc = now;
        storedToken.ReplacedByTokenId = replacement.Entity.Id;
        dbContext.RefreshTokens.Add(replacement.Entity);
        var accessToken = tokenService.CreateAccessToken(storedToken.User);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return ToAuthResponse(storedToken.User, accessToken, replacement);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = refreshTokenService.Hash(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (storedToken is null || storedToken.RevokedAtUtc is not null || storedToken.ExpiresAtUtc <= now) return;
        storedToken.RevokedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<RefreshToken?> FindForUpdateAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? dbContext.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, HOLDLOCK) WHERE [TokenHash] = {tokenHash}")
                .Include(x => x.User).SingleOrDefaultAsync(cancellationToken)
            : dbContext.RefreshTokens.Include(x => x.User)
                .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    private static AuthResponse ToAuthResponse(User user, AccessTokenResult accessToken, RefreshTokenResult refreshToken) =>
        new(accessToken.Token, accessToken.ExpiresAtUtc, refreshToken.RawToken,
            refreshToken.Entity.ExpiresAtUtc, ToResponse(user));

    private static UserResponse ToResponse(User user) => new(
        user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.CreatedAtUtc);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
