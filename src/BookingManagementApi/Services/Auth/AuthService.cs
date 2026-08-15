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
        return new AuthResponse(accessToken.Token, accessToken.ExpiresAtUtc, ToResponse(user));
    }

    private static UserResponse ToResponse(User user) => new(
        user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.CreatedAtUtc);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
